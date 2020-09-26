using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using dnlib.DotNet;
using dnlib.DotNet.Writer;

namespace ASNR
{
    class Program
    {
        private const string InternalInvisibleAttr = "System.Runtime.CompilerServices.InternalsVisibleToAttribute";
        private const char PaddingChar = '-';
        private static Dictionary<string, bool> HandleDic = new Dictionary<string, bool>();
        private static string CurrentPath = "";
        private static string SavePath = "";
        private static string CurrentPublicKey = "";
        private static string CurrentPublicKeyToken = "";
        
        static void Log(string log)
        {
            Console.WriteLine(log);
        }
        
        static void Main(string[] args)
        {
            Console.WriteLine("Auto StrongName Remover/Resigner");
            Console.WriteLine("by Ulysses, wdwxy12345@gmail.com");
            if (args.Length <= 0)
            {
                ShowHelp();
                return;
            }
            if (!File.Exists(args[0]))
            {
                Console.WriteLine("Can not load the target file: First parameter should be a valid path.");
                return;
            }
            bool isResign = false;
            string keyPath = "";
            if (args.Length > 2)
            {
                isResign = args[1].ToLowerInvariant() == "/r";
            }
            try
            {
                if (isResign)
                {
                    Console.WriteLine("<Resign Mode>");
                    ResignStrongName(args[0], args[2], true, true);
                }
                else
                {
                    Console.WriteLine("<Remove Mode>");
                    RemoveStrongName(args[0], true, true);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("[Error] Process failed.");
                Log(ex.ToString());
                return;
            }

            Console.WriteLine("[Done] Press Enter key to exit...");
            Console.ReadLine();
        }

        private static void ShowHelp()
        {
            Console.WriteLine("Usage: asnr.exe <filename> [/r <.snk file path>]");
            Console.WriteLine(
                "This tool will automatically detect all relevant assemblies(EXE/DLL) and remove the target strong name completely.");
            Console.WriteLine("Use /r to switch to resign mode. You need to specify a valid .snk file path.");
        }

        private static void Remove(ModuleDefMD md, StrongNameKey key = null, bool checkBaml = false, int floor = 0)
        {
            //if (WhiteList.Contains(md.Assembly.Name))
            //{
            //    Log($"{md.Assembly.Name} is in WhiteList! It will keep its sign.");
            //}
            bool isResign = key != null;
            PublicKey pk = null;
            if (isResign)
            {
                pk = new PublicKey(key.PublicKey);
            }
            string operation = isResign ? "Resigning" : "Removing";
            Log($"{"".PadLeft(floor, PaddingChar)}{operation} StrongName for assembly [{md.Name}] ...");
            md.IsStrongNameSigned = isResign;
            if (!isResign)
            {
                //if (md.Assembly.PublicKeyToken != null)
                //{
                //    md.Assembly.PublicKeyToken.Data = null;
                //}
                if (md.Assembly.PublicKey != null)
                {
                    md.Assembly.PublicKey = null;
                }
                md.Assembly.HasPublicKey = false;
            }

            HandleDic[md.Assembly.Name] = true;

            if (checkBaml)
            {
                BamlProject b = new BamlProject(md);
                b.RemoveRefKeyToken(CurrentPublicKeyToken, pk?.Token.ToString() ?? "null");
                if (b.BamlFiles.Count > 0)
                {
                    Log($"{"".PadLeft(floor, PaddingChar)}{operation} StrongName for BAMLs in [{md.Name}] ...");
                }
                b.ProcessForRef();
            }
            var attrs = md.Assembly.CustomAttributes.FindAll(InternalInvisibleAttr);
            if (attrs.Any())
            {
                Log($"{"".PadLeft(floor, PaddingChar)}{operation} StrongName for Attributes in [{md.Name}] ...");
            }
            foreach (var attr in attrs)
            {
                var s = (UTF8String)attr.ConstructorArguments[0].Value;
                if (s.Contains("PublicKey=" + CurrentPublicKey))
                {
                    var arg = attr.ConstructorArguments[0];
                    arg.Value = s.Remove(s.IndexOf(","));
                    var name = arg.Value.ToString();
                    if (isResign)
                    {
                        arg.Value += $", {pk.ToString()}";
                    }
                    attr.ConstructorArguments[0] = arg;

                    //Remove InternalVisible
                    if (HandleDic.ContainsKey(name) && HandleDic[name])
                    {
                        continue;
                    }
                    var p = Path.Combine(CurrentPath, name + ".dll");
                    if (!File.Exists(p))
                    {
                        p = Path.Combine(CurrentPath, name + ".exe");
                    }
                    if (File.Exists(p))
                    {
                        try
                        {
                            Remove(ModuleDefMD.Load(p), key, checkBaml, floor + 1);
                        }
                        catch (Exception ex)
                        {
                            Log(ex.ToString());
                        }
                    }
                }
            }
            //Remove AssemblyRef
            Log($"{"".PadLeft(floor, PaddingChar)}{operation} StrongName for AssemblyRefs in [{md.Name}] ...");
            foreach (var asmref in md.GetAssemblyRefs())
            {
                if (asmref.PublicKeyOrToken.Token.ToString() == CurrentPublicKeyToken)
                {
                    var p = Path.Combine(CurrentPath, asmref.Name + ".dll");
                    if (!File.Exists(p))
                    {
                        p = Path.Combine(CurrentPath, asmref.Name + ".exe");
                    }
                    if (File.Exists(p))
                    {
                        if (!isResign)
                        {
                            //if (asmref.PublicKeyOrToken != null)
                            //{
                            //    asmref.PublicKeyOrToken = null;
                            //}
                            asmref.HasPublicKey = false;
                        }
                        else
                        {
                            asmref.PublicKeyOrToken = pk.Token;
                            asmref.HasPublicKey = false;
                        }

                        if (HandleDic.ContainsKey(asmref.Name) && HandleDic[asmref.Name])
                        {
                            continue;
                        }

                        try
                        {
                            Remove(ModuleDefMD.Load(p),key, checkBaml, floor + 1);
                        }
                        catch (Exception ex)
                        {
                            Log(ex.ToString());
                        }
                    }
                }
            }
            Log($"{"".PadLeft(floor, PaddingChar)}Saving [{md.Name}] ...");
            if (isResign)
            {

                if (md.IsILOnly)
                {
                    ModuleWriterOptions option = new ModuleWriterOptions(md);
                    option.InitializeStrongNameSigning(md, key);
                    option.ShareMethodBodies = false;
                    md.Write(Path.Combine(SavePath, md.Name), option);
                }
                else
                {
                    NativeModuleWriterOptions option = new NativeModuleWriterOptions(md, false);
                    option.InitializeStrongNameSigning(md, key);
                    md.NativeWrite(Path.Combine(SavePath, md.Name), option);
                }
            }
            else
            {
                if (md.IsILOnly)
                {
                    md.Write(Path.Combine(SavePath, md.Name));
                }
                else
                {
                    md.NativeWrite(Path.Combine(SavePath, md.Name));
                }
            }
        }

        private static void RemoveStrongName(string path, bool aggressiveCheck = false, bool checkBaml = false)
        {
            ModuleDefMD md;
            try
            {
                md = ModuleDefMD.Load(path);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Failed to Load assembly at {path}.");
                Log(ex.ToString());
                return;
            }
            CurrentPublicKeyToken = md.Assembly.PublicKeyToken.ToString();
            CurrentPublicKey = md.Assembly.PublicKey.ToString();
            CurrentPath = Path.GetDirectoryName(md.Location);
            var unsignedDir = Directory.CreateDirectory(Path.Combine(CurrentPath, "Unsigned"));
            SavePath = unsignedDir.FullName;
            Remove(md, null, checkBaml);

            if (!aggressiveCheck) return;

            DirectoryInfo di = new DirectoryInfo(CurrentPath);
            var files = di.EnumerateFiles("*.dll").Concat(di.EnumerateFiles("*.exe"));
            foreach (var fileInfo in files)
            {
                ModuleDefMD md2;
                try
                {
                    md2 = ModuleDefMD.Load(fileInfo.FullName);
                }
                catch (Exception)
                {
                    continue;
                }
                if (HandleDic.ContainsKey(md2.Assembly.Name) && HandleDic[md2.Assembly.Name])
                {
                    continue;
                }
                if (md2.Assembly.PublicKeyToken == null ||
                    md2.Assembly.PublicKeyToken.ToString() != CurrentPublicKeyToken)
                {
                    continue;
                }
                Remove(md2, null, checkBaml);
            }
        }

        private static void ResignStrongName(string path, string keyFilePath, bool aggressiveCheck = false,
            bool checkBaml = false)
        {
            ModuleDefMD md;
            StrongNameKey key;
            try
            {
                md = ModuleDefMD.Load(path);
                key = new StrongNameKey(keyFilePath);
            }
            catch (InvalidKeyException ex)
            {
                Console.WriteLine($"[ERROR] Strong Name Key at {keyFilePath} is invalid.");
                Log(ex.ToString());
                return;
            }
            catch (FileNotFoundException ex)
            {
                Console.WriteLine($"[ERROR] Failed to Load {ex.FileName}.");
                Log(ex.ToString());
                return;
            }

            //TODO:
            CurrentPublicKeyToken = md.Assembly.PublicKeyToken.ToString();
            CurrentPublicKey = md.Assembly.PublicKey.ToString();
            CurrentPath = Path.GetDirectoryName(md.Location);
            var resignedDir = Directory.CreateDirectory(Path.Combine(CurrentPath, "Resigned"));
            SavePath = resignedDir.FullName;
            Remove(md, key, checkBaml);

            if (!aggressiveCheck) return;

            DirectoryInfo di = new DirectoryInfo(CurrentPath);
            var files = di.EnumerateFiles("*.dll").Concat(di.EnumerateFiles("*.exe"));
            foreach (var fileInfo in files)
            {
                ModuleDefMD md2;
                try
                {
                    md2 = ModuleDefMD.Load(fileInfo.FullName);
                }
                catch (BadImageFormatException)
                {
                    continue;
                }
                if (HandleDic.ContainsKey(md2.Assembly.Name) && HandleDic[md2.Assembly.Name])
                {
                    continue;
                }
                if (md2.Assembly.PublicKeyToken == null ||
                    md2.Assembly.PublicKeyToken.ToString() != CurrentPublicKeyToken)
                {
                    continue;
                }
                Remove(md2, key, checkBaml);
            }
        }
    }
}
