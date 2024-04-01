#!/bin/sh

# Please download bflat v8.x from https://github.com/bflattened/bflat/releases 
# and add it to your path.
# Then, modify this file to suit your source and bin locations.

# Please make sure the C++ Standard Library dependency is installed before 
# building (your package name may vary depending on your distribution -- shown 
# here for Ubuntu 20.04):

# sudo apt update
# sudo apt install libc++-dev

bflat build \
    --out /PropertyProviderConsole/pp \
    -x \
    --no-globalization \
    --define NETCOREAPP \
    --stdlib DotNet \
    --os linux \
    --arch x64 \
    --langversion 11.0 \
    "/PropertyProviderConsole/Program.cs" \
    "/PropertyProviderConsole/Utils.cs"