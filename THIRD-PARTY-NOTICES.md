# Third-party notices

Ducz Map Builder ships the following third-party components. Each keeps its own license;
the texts are available at the linked projects and, where required, are reproduced in the
NuGet packages' license files.

| Component | Version | License | Use |
| --- | --- | --- | --- |
| .NET Runtime (self-contained) | 8.0 | MIT (Microsoft) | application runtime |
| Silk.NET (Windowing, Input, OpenGL, OpenAL bindings) | 2.23.0 | MIT | window, input, graphics API |
| GLFW (via Silk.NET.GLFW.Native) | 3.x | zlib/libpng | window/input backend |
| OpenAL Soft (via Silk.NET.OpenAL.Soft.Native) | 1.23.1 | LGPL-2.0 (dynamically linked as a separate DLL; replaceable) | audio |
| AssimpNet | 4.1.0 | MIT (wrapper) | FBX/OBJ/DAE/STL import |
| Assimp (native library bundled with AssimpNet) | 5.x | BSD-3-Clause | model import |
| SharpGLTF (Core, Toolkit, Runtime) | 1.0.6 | MIT | glTF/GLB import and export |
| StbImageSharp | 2.30.15 | Public domain / MIT | image decoding |
| StbTrueTypeSharp | 1.26.12 | Public domain / MIT | font rasterization |

Ducz Map Builder itself is released under the MIT License (see LICENSE).
