# Auto StrongName Remover / Resigner

by Ulysses

---

A simple tool to remove/resign assemblies' strong name (public key & public key token) automatically. 

It not only removes the strong name information in the target assembly (`AssemblyDef`), but also tracks and removes tokens for all relevant assemblies (`AssemblyRef`), BAMLs (`AssemblyInfo`), and CustomAttributes (Currently support `InternalsVisibleToAttribute`). 

By this way, the StrongName is completely removed from all assemblies (maybe). Mixed assemblies are supported (thanks to dnlib).

Usage:

	asnr.exe <filename(main assembly)> [/r <.snk file path>]

## Thanks
ASNR uses [**dnlib**](https://github.com/0xd4d/dnlib) (by 0xd4d , LICENSE: MIT)

and BAML code is from [**ConfuserEx**](https://github.com/yck1509/ConfuserEx) (by yck1509 , LICENSE: MIT)


## License

LGPL v3

---

by Ulysses , wdwxy12345@gmail.com
