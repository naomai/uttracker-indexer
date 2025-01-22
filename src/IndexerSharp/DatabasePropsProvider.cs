using System;

namespace Naomai.UTT.Indexer;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Utt2Database;


/* 
 * Props stored in database for use in other
 * parts of the stack (ie. frontend)
 * 
 * Replaces "DynConfig.vb"
 */


public class DatabasePropsProvider : PropsProvider
{
	protected Utt2Context _dbCtx;

	public DatabasePropsProvider(Utt2Context context, string ns = "")
	{
		_dbCtx = context;
		_nsName = ns;
	}


	public override string? GetProperty(string key)
	{
		string keyFull = GetFullyQualifiedName(key);
		ConfigProp? prop = _dbCtx.ConfigProps.SingleOrDefault(
			p => p.Key == keyFull, 
			null
		);

		if(prop == null)
		{
			return null;
		}
			
		return prop.Data;
	}

	public override void SetProperty(string key, string data, bool priv=false)
	{
		string keyFull = GetFullyQualifiedName(key);

		ConfigProp? prop = _dbCtx.ConfigProps.SingleOrDefault(p => p.Key == keyFull);

		if (prop == null)
		{
			prop = new ConfigProp { 
				Key = keyFull, 
				Data = data, 
				IsPrivate = priv 
			};
			_dbCtx.ConfigProps.Add(prop);
		}
		else
		{
			prop.Data = data;
			prop.IsPrivate = priv;
			_dbCtx.ConfigProps.Update(prop);
		}
		_dbCtx.SaveChanges();
	}
	public override void UnsetProperty(string key)
	{
		string keyFull = GetFullyQualifiedName(key);
		string keyGroupPrefix = keyFull + ".%";

		List<ConfigProp> propAffected = _dbCtx.ConfigProps.Where(
			p=> p.Key==keyFull || EF.Functions.Like(p.Key, keyGroupPrefix)
		).ToList();

		_dbCtx.ConfigProps.RemoveRange(propAffected);
		_dbCtx.SaveChanges();
	}


	protected override DatabasePropsProvider Clone()
	{
		DatabasePropsProvider newInstance = new DatabasePropsProvider(_dbCtx);
		return newInstance;

    }


}