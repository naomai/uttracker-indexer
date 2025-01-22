using System;

namespace Naomai.UTT.Indexer;

public abstract class PropsProvider
{
    protected string _nsName = "";
	public abstract string? GetProperty(string key);
    public abstract void SetProperty(string key, string data, bool priv = false);
    public abstract void UnsetProperty(string key);

    public virtual string? GetProperty(string key, string? defaultValue = null)
    {
        string? value = GetProperty(key);
        if (value == null && defaultValue != null)
        {
            SetProperty(key, defaultValue);
            return defaultValue;
        }
        return value;
    }

	public PropsProvider Ns(string subNs)
    {
        PropsProvider providerCopy = Clone();
        providerCopy._nsName = _nsName;
        return providerCopy;
    }

    protected abstract PropsProvider Clone();

    protected string GetFullyQualifiedName(string key)
    {
        if (_nsName == "")
        {
            return key;
        }
        return _nsName + "." + key;
    }
}
