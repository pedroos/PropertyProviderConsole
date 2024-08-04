$script:srcDir = pwd
$script:binDir = join-path $srcDir bin

$srcs = @(
    "Program.cs"
    "Utils.cs"
)

if (-not(test-path $binDir)) {
    new-item -itemtype directory -path $binDir | out-null
}

$srcPths = $srcs | % { join-path $srcDir $_ }
if ($none = $srcPths | ? { -not(test-path $_) }) {
    write-error ("Sources not found in the current directory: " + 
        "$($none -join ", ")") -category ReadError
    return 1
}

try { & bflat -v | out-null }
catch [System.Management.Automation.CommandNotFoundException] {
    write-error ("It appears bflat is not installed. Please download a v8.x " + 
        "release from https://github.com/bflattened/bflat/releases and add " + 
        "the executable to path.") -category NotInstalled
    return 2
}

$os = if ([Environment]::OSVersion.Platform -eq "Unix") {
    "linux" } else { "windows" }

$osDef = if ([Environment]::OSVersion.Platform -eq "Unix") {
    "LINUX" } else { "WINDOWS" }

$arch = if ([Environment]::Is64BitProcess) { "x64" } else { "x32" }

$exeExt = if ([Environment]::OSVersion.Platform -eq "Unix") { 
    "" } else { ".exe" }

$outPth = join-path $binDir "pp$($exeExt)"

& bflat build `
    --out $outPth `
    -x `
    --no-globalization `
    --define NETCOREAPP `
    --define $osDef `
    --stdlib DotNet `
    --os $os `
    --arch $arch `
    --langversion 11.0 `
    $srcPths

if ($LastExitCode -eq 0) { gi $outPth }

return $LastExitCode
