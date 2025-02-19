using System;

namespace Naomai.UTT.Indexer;

public abstract class PropsProvider : IPropsProvider
{
    public abstract string? GetProperty(string key);
    public abstract void SetProperty(string key, string data, bool priv = false);
    public abstract void UnsetProperty(string key);

    public string? GetProperty(string key, string defaultValue)
    {
        string? value = GetProperty(key);
        if (value == null && defaultValue != null)
        {
            SetProperty(key, defaultValue);
            return defaultValue;
        }
        return value;
    }

	public IPropsProvider Ns(string subNs)
    {
        PropsProviderScoped providerCopy = new PropsProviderScoped(this, subNs);
        return providerCopy;
    }


    
}

public class PropsProviderScoped : IPropsProvider
{
    protected string _nsName = "";
    protected PropsProvider _provider;

    public PropsProviderScoped (PropsProvider provider, string ns)
    {
        _provider = provider;
        _nsName = ns;
    }

    public string? GetProperty(string key)
    {
        string keyFull = GetFullyQualifiedName(key);
        return _provider.GetProperty(keyFull);
    }

    public string? GetProperty(string key, string defaultValue)
    {
        string keyFull = GetFullyQualifiedName(key);
        return _provider.GetProperty(keyFull, defaultValue);
    }
    public  void SetProperty(string key, string data, bool priv = false)
    {
        string keyFull = GetFullyQualifiedName(key);
        _provider.SetProperty(keyFull, data, priv);
    }
    public void UnsetProperty(string key)
    {
        string keyFull = GetFullyQualifiedName(key);
        _provider.UnsetProperty(keyFull);
    }


    public IPropsProvider Ns(string subNs)
    {
        IPropsProvider providerCopy = _provider.Ns(GetFullyQualifiedName(subNs));
        return providerCopy;
    }

    protected string GetFullyQualifiedName(string key)
    {
        if (_nsName == "")
        {
            return key;
        }
        return _nsName + "." + key;
    }
}

public interface IPropsProvider
{
    string? GetProperty(string key);
    void SetProperty(string key, string data, bool priv = false);
    void UnsetProperty(string key);
    IPropsProvider Ns(string subNs);
}
