# Compila o LiveHelm-App e coloca o programa pronto na pasta "LiveHelm-App", ao lado deste arquivo, com um atalho
# ("LiveHelm-App.lnk"). Feche o programa antes. Uso: dois cliques em Publicar.bat (ou .\publicar.ps1 num terminal).
$ErrorActionPreference = 'Stop'
$nome = 'LiveHelm-App'
$aqui = $PSScriptRoot
$destino = Join-Path $aqui $nome
$saida = Join-Path $aqui 'bin\Release\net48'

Write-Host '1/3 compilando'
dotnet build (Join-Path $aqui 'LiveHelmApp.csproj') -c Release -nologo
if ($LASTEXITCODE -ne 0) { throw 'o build falhou' }

Write-Host '2/3 copiando'
if (Test-Path -LiteralPath $destino) {
    try { Remove-Item -LiteralPath $destino -Recurse -Force }
    catch { throw "nao consegui limpar '$destino': o LiveHelm-App esta aberto?" }
}
New-Item -ItemType Directory -Path $destino | Out-Null
foreach ($arquivo in "$nome.exe", "$nome.exe.config", 'Newtonsoft.Json.dll') { Copy-Item -LiteralPath (Join-Path $saida $arquivo) -Destination $destino }

# O atalho aponta pra uma copia do icone com o nome ligado ao conteudo: o Windows guarda icones por caminho, e com o mesmo
# nome continuaria mostrando o antigo.
$hash = (Get-FileHash -LiteralPath (Join-Path $aqui 'icone.ico') -Algorithm MD5).Hash.Substring(0, 8).ToLower()
$icone = Join-Path $destino "d20-$hash.ico"
Copy-Item -LiteralPath (Join-Path $aqui 'icone.ico') -Destination $icone

Write-Host '3/3 atalho'
$exe = Join-Path $destino "$nome.exe"
$atalho = (New-Object -ComObject WScript.Shell).CreateShortcut((Join-Path $aqui "$nome.lnk"))
$atalho.TargetPath = $exe
$atalho.WorkingDirectory = $destino
$atalho.IconLocation = $icone
$atalho.Save()
Write-Host "pronto: $exe"
