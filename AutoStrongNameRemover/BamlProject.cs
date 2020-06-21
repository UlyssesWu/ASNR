using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Resources;
using ASNR.Analyzers.Baml;
using dnlib.DotNet;

namespace ASNR
{
    class BamlProject
    {
        public readonly string Path = "";
        public readonly Dictionary<string, BamlDocument> BamlFiles = new Dictionary<string, BamlDocument>();
        public string AssemblyName => _assembly?.FullName ?? System.IO.Path.GetFileNameWithoutExtension(Path);

        private ModuleDefMD _assembly;

        public BamlProject(string path)
        {
            Path = path;
            _assembly = ModuleDefMD.Load(path);
        }

        public BamlProject(ModuleDefMD module)
        {
            _assembly = module;
        }

        public void RemoveRefKeyToken(string publicKeyToken, string replaceToken = "null")
        {
            if (publicKeyToken.Length != 16)
            {
                throw new ArgumentException("PublicKeyToken's length should be 16.");
            }

            foreach (var resource in _assembly.Resources)
            {
                if (resource.ResourceType == ResourceType.Embedded)
                {
                    ParseResource((EmbeddedResource) resource);
                }
            }

            foreach (var bamlFile in BamlFiles.Values)
            {
                ParseBamlForRefSign(bamlFile, publicKeyToken, replaceToken);
            }
        }

        private void ParseBamlForRefSign(BamlDocument baml, string keyToken, string replaceToken = "null")
        {
            foreach (var brecord in baml)
            {
                if (brecord.Type == BamlRecordType.AssemblyInfo)
                {
                    var r = (AssemblyInfoRecord) brecord;
                    var name = r.AssemblyFullName;
                    if (!name.Contains("PublicKeyToken=" + keyToken))
                    {
                        continue;
                    }

                    r.AssemblyFullName = name.Replace(keyToken, replaceToken);
                }
            }
        }

        public void ProcessForRef()
        {
            for (int i = 0; i < _assembly.Resources.Count; i++)
            {
                if (_assembly.Resources[i].ResourceType == ResourceType.Embedded)
                {
                    _assembly.Resources[i] = ReloadResource((EmbeddedResource) _assembly.Resources[i]);
                }
            }
        }

        private EmbeddedResource ReloadResource(EmbeddedResource resource)
        {
            ResourceReader reader;
            try
            {
                reader = new ResourceReader(resource.CreateReader().AsStream());
            }
            catch (ArgumentException)
            {
                Console.WriteLine("This resource can not be parsed.");
                return resource;
            }

            MemoryStream m = new MemoryStream();
            ResourceWriter writer = new ResourceWriter(m);

            var e = reader.GetEnumerator();
            while (e.MoveNext())
            {
                if (BamlFiles.ContainsKey(e.Key.ToString()))
                {
                    //MARK:AF 3E 00 00
                    using var ms = new MemoryStream();
                    BamlWriter.WriteDocument(BamlFiles[e.Key.ToString()], ms);
                    writer.AddResource(e.Key.ToString(), ms.ToArray());
                }
                else
                {
                    writer.AddResource(e.Key.ToString(), e.Value);
                }
            }

            //writer.AddResource(e.Key.ToString(), e.Value);
            writer.Generate();
            EmbeddedResource embedded = new EmbeddedResource(resource.Name, m.ToArray());
            writer.Close();
            return embedded;
        }

        private void ParseResource(EmbeddedResource resource)
        {
            ResourceReader reader;
            try
            {
                reader = new ResourceReader(resource.CreateReader().AsStream());
            }
            catch (ArgumentException)
            {
                Console.WriteLine("This resource can not be parsed.");
                //throw;
                return;
            }

            var e = reader.GetEnumerator();
            while (e.MoveNext())
            {
                if (e.Key.ToString().ToLower().EndsWith(".baml"))
                {
                    reader.GetResourceData(e.Key.ToString(), out _, out var contents);

                    //MARK:AF 3E 00 00
                    contents = contents.Skip(4).ToArray(); //MARK:the first 4 bytes = length
                    using (var ms = new MemoryStream(contents))
                    {
                        BamlDocument b = BamlReader.ReadDocument(ms);
                        BamlFiles.TryAdd(e.Key.ToString(), b);
                    }
                }
            }
        }
    }
}