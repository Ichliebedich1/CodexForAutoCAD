# Vendored NuGet dependency

`Microsoft.NETFramework.ReferenceAssemblies.net45` 1.0.3 is vendored solely as a compile-time reference package so Host.2016 can be restored from a clean checkout without consulting user or network NuGet sources.

- Upstream package: https://www.nuget.org/packages/Microsoft.NETFramework.ReferenceAssemblies.net45/1.0.3
- Upstream project/license: https://github.com/microsoft/dotnet (MIT)
- File: `nuget/Microsoft.NETFramework.ReferenceAssemblies.net45.1.0.3.nupkg`
- SHA-256: `23A9F94EA3E2CB88CD8341AF75B811C6FB5CB82516FC696E95ED4620279128E3`
- File SHA-512 (base64): `zPJ5Pqc6+cBg4ir33AWryA8CUxJJj68Cs1Cfo8plZt1HH3Q0B/EqVon6LRXw9b8dfQyLYMqTJJk2maXgLhGJIw==`
- NuGet content hash: `dcSLNuUX2rfZejsyta2EWZ1W5U6ucbFt697lRg1qiTlTM5ZlYv4uAvuxE6ROy6xLWWhLhOaReCDxkhxcajRYtQ==`
- Microsoft author-signing certificate SHA-256: `AA12DA22A49BCE7D5C1AE64CC1F3D892F150DA76140F210ABD2CBFFCA2C18A27`
- NuGet.org repository-signing certificate SHA-256: `5A2901D6ADA3D18260B9C6DFE2133C95D74B9EEF6AE0E5DC334C8454D1477DF4`

The Host.2016 verifier checks the file hashes, package signatures, locked dependency graph, and the Host-project-local `src/Codex.AutoCAD.Host.2016/NuGet.Config` before building.

## MIT License

The MIT License (MIT)

Copyright (c) .NET Foundation and Contributors

All rights reserved.

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
