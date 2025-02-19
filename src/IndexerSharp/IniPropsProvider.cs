using System;
using System.Configuration;
using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using Google.Protobuf.WellKnownTypes;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using Microsoft.Extensions.Configuration.Ini;
using Microsoft.Extensions.FileProviders;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace Naomai.UTT.Indexer;

public class IniPropsProvider : PropsProvider
{
    public string? IniName;
    public IniConfigurationProvider? IniProvider;

    public Stream? templateFileStream;

    public IniPropsProvider(string? sourceFile = null)
    {
        if(sourceFile != null)
        {
            LoadFile(sourceFile);
        }
    }

    public override string? GetProperty(string prop) {
        string? result;

        string propertyAccessor = GetPropertyAccessorString(prop);
        bool hasValue = IniProvider.TryGet(propertyAccessor, out result);

        return result;
    }

    public override void SetProperty(string prop, string value, bool priv = false) {
        string propertyAccessor = GetPropertyAccessorString(prop);
        IniProvider.Set(propertyAccessor, value);
    }

    public override void UnsetProperty(string prop)
    {
        string propertyAccessor = GetPropertyAccessorString(prop);
        IniProvider.Set(propertyAccessor, null);
    }

    public bool PropertyExists(string prop) {
        string propertyAccessor = GetPropertyAccessorString(prop);
        string value;
        bool hasValue = IniProvider.TryGet(propertyAccessor, out value);
        return hasValue;
    }

    protected void LoadFile(string sourceFile)
    {
        string sourceFileReal = Path.GetFullPath(sourceFile);
        string sourceFileDir = Path.GetDirectoryName(sourceFileReal);
        string sourceFileName = Path.GetFileName(sourceFileReal);

        IniName = sourceFileReal;

        if (!File.Exists(IniName)) {
            CreateDefaultConfigFile();
        }

        IniConfigurationSource iniSrc = new IniConfigurationSource
        {
            FileProvider = new PhysicalFileProvider(sourceFileDir),
            Path = sourceFileName
        };

        IniProvider = new IniConfigurationProvider(iniSrc);
        IniProvider.Load();

    }

    protected static string GetPropertyAccessorString(string propertyString){
        string[] propertyChunks = propertyString.Split(".", 2);
        string sectionName = propertyChunks[0].Replace("|", ".");
        string propertyName = propertyChunks[1];
        return sectionName + ":" + propertyName;

    }

    private void CreateDefaultConfigFile()
    {
        if(templateFileStream == null)
        {
            return;
        }
        templateFileStream.CopyTo(File.Create(IniName));
    }




}
