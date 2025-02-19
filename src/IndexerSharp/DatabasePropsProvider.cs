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

	public DatabasePropsProvider(Utt2Context context)
	{
		_dbCtx = context;
	}


	public override string? GetProperty(string key)
	{
		ConfigProp? prop = _dbCtx.ConfigProps.SingleOrDefault(
			p => p.Key == key, 
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
		ConfigProp? prop = _dbCtx.ConfigProps.SingleOrDefault(p => p.Key == key);

		if (prop == null)
		{
			prop = new ConfigProp { 
				Key = key, 
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
		string keyGroupPrefix = key + ".%";

		List<ConfigProp> propAffected = _dbCtx.ConfigProps.Where(
			p=> p.Key== key || EF.Functions.Like(p.Key, keyGroupPrefix)
		).ToList();

		_dbCtx.ConfigProps.RemoveRange(propAffected);
		_dbCtx.SaveChanges();
	}
}