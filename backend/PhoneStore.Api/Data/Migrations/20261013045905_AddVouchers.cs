using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PhoneStore.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddVouchers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Orders_Totals",
                table: "Orders");

            migrationBuilder.AddColumn<string>(
                name: "VoucherCode",
                table: "Orders",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "VoucherDiscountTotal",
                table: "Orders",
                type: "decimal(18,0)",
                precision: 18,
                scale: 0,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "Vouchers",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false),
                    DiscountType = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    DiscountValue = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    MaxDiscount = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: true),
                    MinimumSpend = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    StartsAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    EndsAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    TotalLimit = table.Column<int>(type: "int", nullable: false),
                    PerUserLimit = table.Column<int>(type: "int", nullable: true),
                    ProductIds = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TierIds = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreationKey = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestHash = table.Column<byte[]>(type: "varbinary(32)", maxLength: 32, nullable: false),
                    Version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Vouchers", x => x.Id);
                    table.CheckConstraint("CK_Vouchers_Rules", "[Status] IN ('Draft','Published','Disabled') AND [DiscountType] IN ('Percent','Fixed') AND [DiscountValue] > 0 AND ([DiscountType] <> 'Percent' OR [DiscountValue] <= 100) AND [MinimumSpend] >= 0 AND ([MaxDiscount] IS NULL OR [MaxDiscount] > 0) AND [EndsAt] > [StartsAt] AND [TotalLimit] > 0 AND ([PerUserLimit] IS NULL OR [PerUserLimit] > 0) AND ISJSON([ProductIds])=1 AND ISJSON([TierIds])=1");
                });

            migrationBuilder.CreateTable(
                name: "VoucherUses",
                columns: table => new
                {
                    OrderId = table.Column<long>(type: "bigint", nullable: false),
                    VoucherId = table.Column<long>(type: "bigint", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Released = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VoucherUses", x => x.OrderId);
                    table.ForeignKey(
                        name: "FK_VoucherUses_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_VoucherUses_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_VoucherUses_Vouchers_VoucherId",
                        column: x => x.VoucherId,
                        principalTable: "Vouchers",
                        principalColumn: "Id");
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_Orders_Totals",
                table: "Orders",
                sql: "[Subtotal] >= 0 AND [DiscountTotal] >= 0 AND [ShippingFee] >= 0 AND [GrandTotal] >= 0 AND [VoucherDiscountTotal] >= 0 AND [VoucherDiscountTotal] <= [DiscountTotal] AND [DiscountTotal] <= [Subtotal] AND [GrandTotal] = [Subtotal] - [DiscountTotal] + [ShippingFee]");

            migrationBuilder.CreateIndex(
                name: "IX_Vouchers_Code",
                table: "Vouchers",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Vouchers_CreationKey",
                table: "Vouchers",
                column: "CreationKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VoucherUses_UserId",
                table: "VoucherUses",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_VoucherUses_VoucherId_Released_UserId",
                table: "VoucherUses",
                columns: new[] { "VoucherId", "Released", "UserId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("IF EXISTS (SELECT 1 FROM VoucherUses) THROW 51092, 'Voucher history exists; use a forward migration.', 1;");
            migrationBuilder.DropTable(
                name: "VoucherUses");

            migrationBuilder.DropTable(
                name: "Vouchers");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Orders_Totals",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "VoucherCode",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "VoucherDiscountTotal",
                table: "Orders");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Orders_Totals",
                table: "Orders",
                sql: "[Subtotal] >= 0 AND [DiscountTotal] >= 0 AND [ShippingFee] >= 0 AND [GrandTotal] >= 0 AND [DiscountTotal] <= [Subtotal] AND [GrandTotal] = [Subtotal] - [DiscountTotal] + [ShippingFee]");
        }
    }
}
