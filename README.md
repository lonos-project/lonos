## How the image is build

- Building the compiler. It's pure C#. Requies Mono (Linux) or .NET framework ()
- msbuild / normal compilation of pure C# code into a .NET-Assembly
- Converting the .NET-Assembly into with the Mosa-Compiler to native machine code
- build some requied Assembler-Code  and append it to the native binary. The Assembler code ist mostly used for early initalization.
- Building the Operating System Disk Image, with Grub2 as Bootloader

## License
Lonos is published under the GNU General Public License. This software includes third party open source software components. Each of these software components have their own license. All sourcecode is open source.
