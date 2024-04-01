# Please download bflat v8.x from https://github.com/bflattened/bflat/releases 
# and add it to your path.
# Then, modify this file to suit your source and bin locations.

& bflat build `
    --out D:\PropertyProviderConsole\pp.exe `
    -x `
    --no-globalization `
    --define NETCOREAPP `
    --stdlib DotNet `
    --os windows `
    --arch x64 `
    --langversion 11.0 `
    "D:\PropertyProviderConsole\Program.cs" `
    "D:\PropertyProviderConsole\Utils.cs"