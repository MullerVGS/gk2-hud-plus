# Controle da instância green (D:\GK2-green). Nunca toca no processo blue (pasta Steam).
param(
    [ValidateSet('start', 'stop', 'status', 'hide', 'show', 'sync-settings')]
    [string]$Action = 'status',
    [string]$GreenDir = 'D:\GK2-green'
)
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
$exe = Join-Path $GreenDir 'GraveyardKeeper2.exe'
$blueKey = 'HKCU:\Software\Lazy Bear Games\Graveyard Keeper 2'
$greenKey = 'HKCU:\Software\Lazy Bear Games\GK2 Green Instance'
$settingsName = 'settings_h1277500064'

Add-Type @"
using System; using System.Runtime.InteropServices;
public class GkGreenWin {
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
  [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
}
"@
[GkGreenWin]::SetProcessDPIAware() | Out-Null

function Get-Green {
    Get-Process GraveyardKeeper2 -ErrorAction SilentlyContinue | Where-Object { $_.Path -and $_.Path.StartsWith($GreenDir, [StringComparison]::OrdinalIgnoreCase) }
}

function Move-GreenWindow([int]$x, [int]$y) {
    $p = Get-Green
    if (-not $p) { throw 'green não está rodando' }
    $h = $p.MainWindowHandle
    $r = New-Object GkGreenWin+RECT; [GkGreenWin]::GetWindowRect($h, [ref]$r) | Out-Null
    # SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE
    [GkGreenWin]::SetWindowPos($h, [IntPtr]::Zero, $x, $y, 0, 0, 0x0001 -bor 0x0004 -bor 0x0010) | Out-Null
}

function Sync-Settings {
    # Copia as opções do blue (idioma, resolução, teclas) e força janela sem som no green.
    $raw = (Get-ItemProperty $blueKey).$settingsName
    $json = [Text.Encoding]::UTF8.GetString($raw).Trim([char]0) | ConvertFrom-Json
    $json.screenMode = 1
    $json.masterVolume = 0.0
    if (-not (Test-Path $greenKey)) { New-Item $greenKey -Force | Out-Null }
    $bytes = [Text.Encoding]::UTF8.GetBytes(($json | ConvertTo-Json -Depth 10 -Compress) + [char]0)
    Set-ItemProperty $greenKey -Name $settingsName -Value $bytes -Type Binary
    Set-ItemProperty $greenKey -Name 'Screenmanager Fullscreen mode_h3630240806' -Value 3 -Type DWord
    Set-ItemProperty $greenKey -Name 'Screenmanager Resolution Window Width_h2524650974' -Value 1920 -Type DWord
    Set-ItemProperty $greenKey -Name 'Screenmanager Resolution Window Height_h1684712807' -Value 1080 -Type DWord
}

switch ($Action) {
    'status' {
        $p = Get-Green
        if ($p) { "green rodando pid=$($p.Id) titulo='$($p.MainWindowTitle)' MB=$([int]($p.WorkingSet64/1MB))" } else { 'green parado' }
    }
    'stop' {
        $p = Get-Green
        if ($p) { $p | Stop-Process -Force; $p.WaitForExit(15000) | Out-Null; 'green parado' } else { 'green já estava parado' }
    }
    'sync-settings' { Sync-Settings; 'settings sincronizados' }
    'start' {
        if (Get-Green) { throw 'green já está rodando (use stop antes)' }
        Sync-Settings
        $p = Start-Process -FilePath $exe -WorkingDirectory $GreenDir -PassThru -ArgumentList '-screen-fullscreen', '0', '-screen-width', '1920', '-screen-height', '1080'
        $deadline = (Get-Date).AddSeconds(60)
        do { Start-Sleep -Milliseconds 300; $p.Refresh() } while ($p.MainWindowHandle -eq [IntPtr]::Zero -and (Get-Date) -lt $deadline)
        Start-Sleep -Seconds 2
        Move-GreenWindow 4000 0
        "green iniciado pid=$($p.Id) (janela fora da tela)"
    }
    'hide' { Move-GreenWindow 4000 0; 'janela green fora da tela' }
    'show' { Move-GreenWindow 0 0; 'janela green no monitor principal' }
}
