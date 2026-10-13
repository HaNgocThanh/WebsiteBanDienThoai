using System.Net;
using System.Security.Cryptography;
using System.Text;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using PhoneStore.Api.Services.Auth;
using PhoneStore.Api.Services.Notifications;

namespace PhoneStore.Api.Services.Email;

public sealed class GmailEmailSettings(IConfiguration configuration)
{
    public string Address => configuration["Email:Address"] ?? "";
    public string Name => configuration["Email:SenderName"] ?? "PhoneStore";
    public string Password => (configuration["Email:AppPassword"] ?? "").Replace(" ", "");
    public bool IsConfigured => MailboxAddress.TryParse(Address, out var mailbox) && mailbox.Address == Address
        && !string.IsNullOrWhiteSpace(Password) && TryOrigin(out _);
    private bool TryOrigin(out Uri? origin) => Uri.TryCreate(configuration["Email:WebsiteUrl"], UriKind.Absolute, out origin)
        && origin.Scheme == "https" && string.IsNullOrEmpty(origin.UserInfo) && origin.AbsolutePath == "/"
        && string.IsNullOrEmpty(origin.Query) && string.IsNullOrEmpty(origin.Fragment);
    public string Link(string path)
    {
        if (!TryOrigin(out var origin) || !path.StartsWith('/') || path.StartsWith("//") || path.Contains('\\')
            || path.Any(char.IsControl)) throw new InvalidOperationException("Invalid email website link.");
        return origin!.GetLeftPart(UriPartial.Authority) + path;
    }
}

// Per-delivery client: singleton adapter never shares an SMTP connection between requests.
public interface IEmailTransport
{
    Task SendAsync(MimeMessage message, CancellationToken ct);
}
public sealed class GmailTransport(GmailEmailSettings settings) : IEmailTransport
{
    public async Task SendAsync(MimeMessage message, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        try
        {
            using var client = new SmtpClient { Timeout = 15000 };
            await client.ConnectAsync("smtp.gmail.com", 587, SecureSocketOptions.StartTls, timeout.Token);
            await client.AuthenticateAsync(settings.Address, settings.Password, timeout.Token);
            await client.SendAsync(message, timeout.Token);
            // Successful DATA acceptance is delivery success even if closing the connection fails.
            try { await client.DisconnectAsync(true, timeout.Token); } catch (Exception) { }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw new OperationCanceledException(ct);
        }
        catch (Exception)
        {
            // Provider exceptions may contain recipient/credentials. Keep them out of middleware/logs.
            throw new InvalidOperationException("EMAIL_DELIVERY_FAILED");
        }
    }
}

public sealed class GmailEmailSender(GmailEmailSettings settings, IEmailTransport transport) : IAuthEmailSender, IOrderEmailSender
{
    public bool IsConfigured => settings.IsConfigured;
    public Task SendAsync(AuthEmail message, CancellationToken ct)
    {
        var title = message.Purpose switch
        {
            "verify-email" => "Xác minh tài khoản PhoneStore",
            "reset-password" => "Đặt lại mật khẩu PhoneStore",
            _ => throw new InvalidOperationException("Unsupported email purpose.")
        };
        var path = "/auth/" + message.Purpose + "#userId=" + message.UserId + "&token=" + Uri.EscapeDataString(message.Token);
        return SendAsync(message.Email, title, "Bấm liên kết bên dưới để tiếp tục. Nếu bạn không yêu cầu thao tác này, hãy bỏ qua thư.",
            path, message.ExpiresAt, null, ct);
    }
    public Task SendAsync(OrderEmail message, CancellationToken ct)
    {
        var title = message.Purpose switch
        {
            "OrderPlaced" or "ViewOrder" => "Thông tin đơn hàng",
            "AccountOrderLookup" => "Tra cứu đơn hàng — đăng nhập để xem",
            "ClaimOrder" => "Nhận quyền sở hữu đơn hàng",
            "CancelOrder" => "Truy cập để hủy đơn hàng",
            "PaymentInitiated" => "Đang chờ thanh toán",
            "PaymentConfirmed" => "Đã ghi nhận thanh toán",
            "OrderStateChanged" => "Trạng thái đơn hàng đã cập nhật",
            "OrderCompleted" => "Đơn hàng đã hoàn tất",
            "OrderPaymentExpired" => "Đơn hàng hết hạn thanh toán",
            "CancellationRequested" => "Đã tiếp nhận yêu cầu hủy đơn",
            "CancellationReviewed" => "Yêu cầu hủy đơn đã được xử lý",
            "RefundCreated" => "Đã tạo yêu cầu hoàn tiền",
            "RefundCompleted" => "Đã ghi nhận hoàn tiền",
            "RefundFailed" => "Hoàn tiền chưa thành công",
            "OrderClaimed" => "Đã nhận quyền sở hữu đơn hàng",
            _ => "Đơn hàng có cập nhật"
        };
        var state = message.Status switch
        {
            "Placed" => "Đã đặt hàng", "Reviewing" => "Đang xem xét", "Approved" => "Đã xác nhận",
            "Shipping" => "Đang giao hàng", "Delivered" => "Đã giao hàng", "Completed" => "Đã hoàn tất",
            "Cancelled" => "Đã hủy", _ => null
        };
        var body = "Đơn hàng " + message.OrderNumber + ". " + title + "." + (state is null ? "" : " Trạng thái: " + state + ".")
            + (message.Purpose == "AccountOrderLookup" ? " Đơn này thuộc tài khoản. Vui lòng đăng nhập bằng tài khoản sở hữu đơn để xem chi tiết." : "")
            + (message.AccountSetup ? " Bạn có thể đăng ký và xác minh tài khoản để nhận quyền sở hữu đơn." : "");
        return SendAsync(message.Email, title + " — " + message.OrderNumber, body,
            message.ActionPath ?? "/guest/lookup", message.ExpiresAt, message.MessageKey, ct);
    }
    private Task SendAsync(string to, string title, string body, string path, DateTime? expires, string? key, CancellationToken ct)
    {
        if (!IsConfigured) throw new InvalidOperationException("Email adapter unavailable.");
        var link = settings.Link(path);
        var expiry = expires is null ? "" : " Liên kết hết hạn lúc " + expires.Value.ToUniversalTime().ToString("yyyy-MM-dd HH:mm 'UTC'") + ".";
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(settings.Name, settings.Address));
        message.To.Add(MailboxAddress.Parse(to)); message.Subject = title;
        if (key is not null) message.MessageId = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key))) + "@phonestore.mail";
        static string H(string value) => WebUtility.HtmlEncode(value);
        message.Body = new BodyBuilder
        {
            TextBody = body + expiry + "\n\n" + link,
            HtmlBody = "<!doctype html><html lang=\"vi\"><body><h1>PhoneStore</h1><h2>" + H(title)
                + "</h2><p>" + H(body + expiry) + "</p><p><a href=\"" + H(link) + "\">Mở liên kết</a></p></body></html>"
        }.ToMessageBody();
        return transport.SendAsync(message, ct);
    }
}
