$ErrorActionPreference = 'Stop'
$modelPath = Join-Path $PSScriptRoot 'src/ControllerLabPro/Models/yolox_nano.onnx'
$expected = 'c789161ed43c8269fcd4e67c67eeeb4e80c622da2eb296a20bc6007bd18a0b7d'
if (!(Test-Path -LiteralPath $modelPath) -or (Get-FileHash -LiteralPath $modelPath -Algorithm SHA256).Hash.ToLowerInvariant() -ne $expected) {
    Invoke-WebRequest 'https://github.com/Megvii-BaseDetection/YOLOX/releases/download/0.1.1rc0/yolox_nano.onnx' -OutFile $modelPath
}
if ((Get-FileHash -LiteralPath $modelPath -Algorithm SHA256).Hash.ToLowerInvariant() -ne $expected) { throw 'YOLOX model checksum mismatch; refusing to build.' }
Write-Output 'Official YOLOX Nano model verified.'

$modelPath = Join-Path $PSScriptRoot 'src/ControllerLabPro/Models/yolox_s.onnx'
$expected = 'c5c2d13e59ae883e6af3b45daea64af4833a4951c92d116ec270d9ddbe998063'
if (!(Test-Path -LiteralPath $modelPath) -or (Get-FileHash -LiteralPath $modelPath -Algorithm SHA256).Hash.ToLowerInvariant() -ne $expected) {
    Invoke-WebRequest 'https://github.com/Megvii-BaseDetection/YOLOX/releases/download/0.1.1rc0/yolox_s.onnx' -OutFile $modelPath
}
if ((Get-FileHash -LiteralPath $modelPath -Algorithm SHA256).Hash.ToLowerInvariant() -ne $expected) { throw 'YOLOX-S model checksum mismatch; refusing to build.' }
Write-Output 'Official YOLOX-S model verified.'
