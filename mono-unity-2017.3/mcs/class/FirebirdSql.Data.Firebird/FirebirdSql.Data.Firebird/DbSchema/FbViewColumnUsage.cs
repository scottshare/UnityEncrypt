/*
 *	Firebird ADO.NET Data provider for .NET and Mono 
 * 
 *	   The contents of this file are subject to the Initial 
 *	   Developer's Public License Version 1.0 (the "License"); 
 *	   you may not use this file except in compliance with the 
 *	   License. You may obtain a copy of the License at 
 *	   http://www.firebirdsql.org/index.php?op=doc&id=idpl
 *
 *	   Software distributed under the License is distributed on 
 *	   an "AS IS" basis, WITHOUT WARRANTY OF ANY KIND, either 
 *	   express or implied. See the License for the specific 
 *	   language governing rights and limitations under the License.
 * 
 *	Copyright (c) 2002, 2005 Carlos Guzman Alvarez
 *	All Rights Reserved.
 */

using System;
using System.Data;
using System.Globalization;
using System.Text;

using FirebirdSql.Data.Common;

namespace FirebirdSql.Data.Firebird.DbSchema
{
	internal class FbViewColumnUsage : FbDbSchema
	{
		#region Constructors

		public FbViewColumnUsage() : base("ViewColumnUsage")
		{
		}

		#endregion

		#region Protected Methods

		protected override StringBuilder GetCommandText(object[] restrictions)
		{
			StringBuilder sql = new StringBuilder();
			StringBuilder where = new StringBuilder();

			sql.Append(
				@"SELECT " +
					"null AS VIEW_CATALOG, " +
					"null AS VIEW_SCHEMA, " +
					"rel.rdb$relation_name AS VIEW_NAME, " +
					"rfr.rdb$field_name AS COLUMN_NAME, " +
					"null AS COLUMN_DATA_TYPE, " +
					"fld.rdb$field_sub_type AS COLUMN_SUB_TYPE, " +
					"cast(fld.rdb$field_length AS integer) AS COLUMN_SIZE, " +
					"cast(fld.rdb$field_precision AS integer) AS NUMERIC_PRECISION, " +
					"cast(fld.rdb$field_scale AS integer) AS NUMERIC_SCALE, " +
                    "cast(fld.rdb$character_length AS integer) AS \"CHARACTER_LENGTH\", " +
					"cast(fld.rdb$field_length AS integer) AS CHARACTER_OCTET_LENGTH, " +
					"rfr.rdb$field_position AS ORDINAL_POSITION, " +
					"fld.rdb$default_source AS COLUMN_DEFAULT, " +
					"fld.rdb$null_flag AS IS_NULLABLE, " +
					"fld.rdb$dimensions AS IS_ARRAY, " +
					"0 AS IS_READONLY, " +
					"fld.rdb$field_type AS FIELD_TYPE, " +
					"null AS CHARACTER_SET_CATALOG, " +
					"null AS CHARACTER_SET_SCHEMA, " +
					"cs.rdb$character_set_name AS CHARACTER_SET_NAME, " +
					"null AS COLLATION_CATALOG, " +
					"null AS COLLATION_SCHEMA, " +
					"coll.rdb$collation_name AS COLLATION_NAME, " +
					"rfr.rdb$description AS DESCRIPTION " +
				"FROM " +
					"rdb$relations rel " +
					"left join rdb$relation_fields rfr ON rel.rdb$relation_name = rfr.rdb$relation_name " +
					"left join rdb$fields fld ON rfr.rdb$field_source = fld.rdb$field_name " +
					"left join rdb$character_sets cs ON cs.rdb$character_set_id = fld.rdb$character_set_id " +
					"left join rdb$collations coll ON (coll.rdb$collation_id = fld.rdb$collation_id AND coll.rdb$character_set_id = fld.rdb$character_set_id)");

			where.Append("rel.rdb$view_source IS NOT NULL");

			if (restrictions != null)
			{
				int index = 0;

				/* VIEW_CATALOG	*/
				if (restrictions.Length >= 1 && restrictions[0] != null)
				{
				}

				/* VIEW_SCHEMA */
				if (restrictions.Length >= 2 && restrictions[1] != null)
				{
				}

				/* VIEW_NAME */
				if (restrictions.Length >= 3 && restrictions[2] != null)
				{
					where.AppendFormat(CultureInfo.CurrentCulture, " AND rel.rdb$relation_name = @p{0}", index++);
				}
			}

			if (where.Length > 0)
			{
				sql.AppendFormat(CultureInfo.CurrentCulture, " WHERE {0} ", where.ToString());
			}

			sql.Append(" ORDER BY rel.rdb$relation_name, rfr.rdb$field_position");

			return sql;
		}

		protected override DataTable ProcessResult(DataTable schema)
		{
			schema.BeginLoadData();

			foreach (DataRow row in schema.Rows)
			{
				int blrType = Convert.ToInt32(row["FIELD_TYPE"], CultureInfo.InvariantCulture);

				int subType = 0;
				if (row["COLUMN_SUB_TYPE"] != System.DBNull.Value)
				{
					subType = Convert.ToInt32(row["COLUMN_SUB_TYPE"], CultureInfo.InvariantCulture);
				}

				int scale = 0;
				if (row["NUMERIC_SCALE"] != System.DBNull.Value)
				{
					scale = Convert.ToInt32(row["NUMERIC_SCALE"], CultureInfo.InvariantCulture);
				}

				if (row["IS_NULLABLE"] == DBNull.Value)
				{
					row["IS_NULLABLE"] = true;
				}
				else
				{
					row["IS_NULLABLE"] = false;
				}

				if (row["IS_ARRAY"] == DBNull.Value ||
					Convert.ToInt32(row["IS_ARRAY"], CultureInfo.InvariantCulture) == 0)
				{
					row["IS_ARRAY"] = false;
				}
				else
				{
					row["IS_ARRAY"] = true;
				}

				FbDbType dbType = (FbDbType)TypeHelper.GetDbDataType(blrType, subType, scale);
				row["COLUMN_DATA_TYPE"] = TypeHelper.GetDataTypeName((DbDataType)dbType).ToLower(CultureInfo.CurrentCulture);

				if (dbType == FbDbType.Char || dbType == FbDbType.VarChar)
				{
					row["COLUMN_SIZE"] = row["CHARACTER_LENGTH"];
				}
				else
				{
					row["CHARACTER_OCTET_LENGTH"] = 0;
				}

				if (dbType == FbDbType.Binary || dbType == FbDbType.Text)
				{
					row["COLUMN_SIZE"] = Int32.MaxValue;
				}

				if (row["NUMERIC_PRECISION"] == System.DBNull.Value &&
				   (dbType == FbDbType.Decimal || dbType == FbDbType.Numeric))
				{
					row["NUMERIC_PRECISION"] = row["COLUMN_SIZE"];
				}

				row["NUMERIC_SCALE"] = (-1) * scale;
			}

			schema.EndLoadData();
			schema.AcceptChanges();

			// Remove not more needed columns
			schema.Columns.Remove("FIELD_TYPE");
			schema.Columns.Remove("CHARACTER_LENGTH");

			return schema;
		}


		#endregion
	}
}