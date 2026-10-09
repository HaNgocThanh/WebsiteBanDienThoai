param([datetime]$AsOf = [datetime]'2026-10-09')
$ErrorActionPreference = 'Stop'
$source = 'https://danhmuchanhchinh.nso.gov.vn/DMDVHC.asmx'
function Get-CatalogRows([string]$Operation, [string]$Filters = '') {
    $date = $AsOf.ToString('dd/MM/yyyy', [Globalization.CultureInfo]::InvariantCulture)
    $envelope = '<soap:Envelope xmlns:soap="http://schemas.xmlsoap.org/soap/envelope/"><soap:Body><' + $Operation + ' xmlns="http://tempuri.org/"><DenNgay>' + $date + '</DenNgay>' + $Filters + '</' + $Operation + '></soap:Body></soap:Envelope>'
    $response = Invoke-WebRequest $source -Method Post -ContentType 'text/xml; charset=utf-8' -Headers @{ SOAPAction = '"http://tempuri.org/' + $Operation + '"' } -Body ([Text.Encoding]::UTF8.GetBytes($envelope))
    $settings = [Xml.XmlReaderSettings]::new()
    $settings.DtdProcessing = [Xml.DtdProcessing]::Prohibit
    $reader = [Xml.XmlReader]::Create([IO.StringReader]::new($response.Content), $settings)
    try { $document = [Xml.XmlDocument]::new(); $document.Load($reader); return $document.SelectNodes('//DocumentElement/TABLE') } finally { $reader.Dispose() }
}
$provinces = @(Get-CatalogRows 'DanhMucTinh' | ForEach-Object { [ordered]@{ code = [string]$_.MaTinh; name = [string]$_.TenTinh } })
$wards = @(Get-CatalogRows 'DanhMucPhuongXa' '<Tinh></Tinh><TenTinh></TenTinh><QuanHuyen></QuanHuyen><TenQuanHuyen></TenQuanHuyen>' | ForEach-Object { [ordered]@{ code = [string]$_.MaPhuongXa; name = [string]$_.TenPhuongXa; provinceCode = [string]$_.MaTinh } })
if ($provinces.Count -lt 1 -or $wards.Count -lt $provinces.Count) { throw 'Danh mục rỗng hoặc thiếu cấp xã.' }
if (@($provinces.code | Select-Object -Unique).Count -ne $provinces.Count -or @($wards.code | Select-Object -Unique).Count -ne $wards.Count) { throw 'Mã hành chính trùng.' }
foreach ($p in $provinces) { if ($p.code -notmatch '^\d{2}$' -or !$p.name -or !($wards.provinceCode -contains $p.code)) { throw 'Tỉnh không hợp lệ hoặc thiếu cấp xã.' } }
foreach ($w in $wards) { if ($w.code -notmatch '^\d{5}$' -or !$w.name -or !($provinces.code -contains $w.provinceCode)) { throw 'Đơn vị cấp xã hoặc tỉnh cha không hợp lệ.' } }
$snapshot = [ordered]@{ asOf = $AsOf.ToString('yyyy-MM-dd'); source = $source; provinces = @($provinces | Sort-Object code); wards = @($wards | Sort-Object code) }
$destination = Join-Path $PSScriptRoot '../backend/PhoneStore.Api/Resources/vietnam-administrative-units.json'
[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($destination)) | Out-Null
[IO.File]::WriteAllText($destination, ($snapshot | ConvertTo-Json -Depth 5) + [Environment]::NewLine, [Text.UTF8Encoding]::new($false))
Write-Output ('Snapshot {0}: {1} tỉnh/thành, {2} đơn vị cấp xã.' -f $snapshot.asOf, $provinces.Count, $wards.Count)
