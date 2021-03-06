/*                 
 *            FirebirdSql.EntityFrameworkCore.Firebird
 *              https://www.firebirdsql.org/en/net-provider/ 
 *     Permission to use, copy, modify, and distribute this software and its
 *     documentation for any purpose, without fee, and without a written
 *     agreement is hereby granted, provided that the above copyright notice
 *     and this paragraph and the following two paragraphs appear in all copies. 
 * 
 *     The contents of this file are subject to the Initial
 *     Developer's Public License Version 1.0 (the "License");
 *     you may not use this file except in compliance with the
 *     License. You may obtain a copy of the License at
 *     http://www.firebirdsql.org/index.php?op=doc&id=idpl
 *
 *     Software distributed under the License is distributed on
 *     an "AS IS" basis, WITHOUT WARRANTY OF ANY KIND, either
 *     express or implied.  See the License for the specific
 *     language governing rights and limitations under the License.
 *
 *      Credits: Rafael Almeida (ralms@ralms.net)
 *                              Sergipe-Brazil
 *                  All Rights Reserved.
 */
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Utilities; 
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Internal; 

namespace Microsoft.EntityFrameworkCore.Update.Internal
{
    public class FbUpdateSqlGenerator : UpdateSqlGenerator, IFbUpdateSqlGenerator
    {
        private StringBuilder _headBlockStringBuilder = null;
        private readonly IRelationalTypeMapper _typeMapperRelational;
        public FbUpdateSqlGenerator(
            [NotNull] UpdateSqlGeneratorDependencies dependencies, 
            [NotNull] IRelationalTypeMapper typeMapper)
            : base(dependencies)
        {
            _typeMapperRelational = typeMapper; 

        }
        

        public override ResultSetMapping AppendInsertOperation(
           StringBuilder commandStringBuilder,
           ModificationCommand command,
           int commandPosition)
        {
            Check.NotNull(command, nameof(command));
            _headBlockStringBuilder = new StringBuilder();
            return AppendBlockInsertOperation(commandStringBuilder, _headBlockStringBuilder, new[] { command }, commandPosition);
        }
         
       
        public ResultSetMapping AppendBlockInsertOperation(StringBuilder commandStringBuilder, StringBuilder headBlockStringBuilder, IReadOnlyList<ModificationCommand> modificationCommands,
            int commandPosition)
        {
            Check.NotNull(commandStringBuilder, nameof(commandStringBuilder));
            Check.NotEmpty(modificationCommands, nameof(modificationCommands));  

            commandStringBuilder.Clear();
            commandStringBuilder.Append("EXECUTE BLOCK ");
            commandStringBuilder.AppendLine("(");
            var commaAppend = string.Empty;
            foreach (var commandNotification in modificationCommands)
            {
                var name = commandNotification.TableName;
                var schema = commandNotification.Schema;
                var operations = commandNotification.ColumnModifications;
                var writeOperations = operations.Where(o => o.IsWrite).ToArray();
                var readOperations = operations.Where(o => o.IsRead).ToArray(); 
                if (writeOperations.Any())
                {  
                    AppendBlockVariable(commandStringBuilder, writeOperations, commaAppend);
                    //Implementation Future!
                    //AppendBlockVariable(headBlockStringBuilder, writeOperations, commaAppend);
                    commaAppend = ","; 
                }
            }
            commandStringBuilder.AppendLine(")");
            var opc = modificationCommands.Select(p => p.ColumnModifications); 
            var ReadNotification =  opc.Last().Where(p=>p.IsRead);
            if (ReadNotification.Count()>0)
            {
                var _column = ReadNotification.Where(p => p.IsKey).FirstOrDefault();
                if (_column != null)
                    commandStringBuilder.Append($"RETURNS (AffectedRows {GetDataType(_column.Property)}) ");
            }
            commandStringBuilder.AppendLine(" AS BEGIN");
            for (var i = 0; i < modificationCommands.Count; i++)
            {
                var name = modificationCommands[i].TableName;
                var schema = modificationCommands[i].Schema;
                var operations = modificationCommands[i].ColumnModifications;
                var writeOperations = operations.Where(o => o.IsWrite).ToArray();
                var readOperations = operations.Where(o => o.IsRead).ToArray(); 
                var modified = modificationCommands[i].ColumnModifications.Where(o => o.IsWrite).ToList();
                AppendInsertCommandHeader(commandStringBuilder, name, schema, writeOperations);
                AppendValuesHeader(commandStringBuilder, modified);
                AppendValuesInsert(commandStringBuilder, modified);
                if (readOperations.Length > 0)
                    AppendInsertOutputClause(commandStringBuilder, name, schema, readOperations, operations);
                else
                {
                    commandStringBuilder.AppendLine(SqlGenerationHelper.StatementTerminator);
                    commandStringBuilder.AppendLine("END;");
                    return ResultSetMapping.LastInResultSet;
                }

            }
            commandStringBuilder.AppendLine("END;");  
            return  ResultSetMapping.NotLastInResultSet;
        }

        private void AppendBlockVariable(StringBuilder commandStringBuilder, IReadOnlyList<ColumnModification> operations,string commaAppend)
        {
           
            foreach (var column in operations)
            {
                var _type = GetDataType(column.Property);
                if (!_type.Equals("CHAR(16) CHARACTER SET OCTETS", StringComparison.InvariantCultureIgnoreCase))
                {
                    commandStringBuilder.Append(commaAppend);
                    commandStringBuilder.AppendLine($"{column.ParameterName}  {_type}=@{column.ParameterName}");
                    commaAppend = ",";
                }
            }
        }

        private void AppendValuesInsert(StringBuilder commandStringBuilder, IReadOnlyList<ColumnModification> operations)
        {
            if (operations.Count > 0)
            {
                commandStringBuilder
                    .Append("(")
                    .AppendJoin(
                        operations,
                        SqlGenerationHelper,
                        (sb, o, helper) =>
                        {
                            var property = GetDataType(o.Property);
                          
                            if (o.IsWrite)
                            {
                                switch (property)
                                {
                                    case "CHAR(16) CHARACTER SET OCTETS":
                                        sb.Append($"CHAR_TO_UUID('{o.Value}')");
                                        break;
                                    default:
                                        sb.Append(":").Append(o.ParameterName);
                                        break;
                                }
                            } 
                        })
                    .Append(")");
            }
        }

        public override ResultSetMapping AppendUpdateOperation(
            StringBuilder commandStringBuilder,
            ModificationCommand command,
            int commandPosition)
        => AppendBlockUpdateOperation(commandStringBuilder, new[] { command }, commandPosition);


        public override ResultSetMapping AppendDeleteOperation(
           StringBuilder commandStringBuilder,
           ModificationCommand command,
           int commandPosition)
        => AppendBlockDeleteOperation(commandStringBuilder, new[] { command }, commandPosition);


        public ResultSetMapping AppendBlockUpdateOperation(StringBuilder commandStringBuilder, IReadOnlyList<ModificationCommand> modificationCommands,
            int commandPosition)
        {
            Check.NotNull(commandStringBuilder, nameof(commandStringBuilder));
            Check.NotEmpty(modificationCommands, nameof(modificationCommands));
            var name = modificationCommands[0].TableName;
            var schema = modificationCommands[0].Schema;
            commandStringBuilder.Clear();
            commandStringBuilder.Append("EXECUTE BLOCK ");
            if (modificationCommands.Any())
            {
                commandStringBuilder.AppendLine("(");
                var separator = string.Empty;
                for (var i = 0; i < modificationCommands.Count; i++)
                {
                    var operations = modificationCommands[i].ColumnModifications;
                    var writeOperations = operations.Where(o => o.IsWrite).ToArray();
                    var readOperations = operations.Where(o => o.IsRead).ToArray();
                    foreach (var param in writeOperations)
                    {
                        commandStringBuilder.Append(separator);
                        commandStringBuilder.Append(param.ParameterName.Replace("@", string.Empty));
                        commandStringBuilder.Append(" ");
                        commandStringBuilder.Append($" {GetDataType(param.Property)}");
                        commandStringBuilder.Append($" = @{param.ParameterName}");
                        separator = ", ";
                    }
                }
                commandStringBuilder.AppendLine();
                commandStringBuilder.Append(") ");
                commandStringBuilder.AppendLine("RETURNS (AffectedRows INT) AS BEGIN")
                                   .AppendLine("AffectedRows=0;");
            }


            for (var i = 0; i < modificationCommands.Count; i++)
            {
                var operations = modificationCommands[i].ColumnModifications;
                var writeOperations = operations.Where(o => o.IsWrite).ToArray();
                var readOperations = operations.Where(o => o.IsRead).ToArray();
                var condicoes = operations.Where(o => o.IsCondition).ToArray();
                commandStringBuilder.Append($"UPDATE {SqlGenerationHelper.DelimitIdentifier(name)} SET ")
                .AppendJoinUpadate(
                    writeOperations,
                    SqlGenerationHelper,
                    (sb, o, helper) =>
                    {
                        if (o.IsWrite)
                            sb.Append($"{SqlGenerationHelper.DelimitIdentifier(o.ColumnName)}=:{o.ParameterName} ");
                    });

                AppendWhereClauseCustoom(commandStringBuilder, condicoes);
                commandStringBuilder.AppendLine(SqlGenerationHelper.StatementTerminator);
                AppendUpdateOutputClause(commandStringBuilder);
            }
            commandStringBuilder.AppendLine("SUSPEND;");
            commandStringBuilder.AppendLine("END;");
            return ResultSetMapping.NotLastInResultSet;
        }

        private void AppendWhereClauseCustoom(StringBuilder commandStringBuilder, ColumnModification[] col)
        {
            if (col.Any())
            {
                commandStringBuilder.Append(" WHERE ");
                var And = string.Empty;
                foreach (var item in col)
                {
                    commandStringBuilder
                        .Append(And)
                        .Append(SqlGenerationHelper.DelimitIdentifier(item.ColumnName))
                        .Append("=")
                        .Append(item.Value);
                }
            }


        }

        public ResultSetMapping AppendBlockDeleteOperation(StringBuilder commandStringBuilder, IReadOnlyList<ModificationCommand> modificationCommands,
           int commandPosition)
        {
            Check.NotNull(commandStringBuilder, nameof(commandStringBuilder));
            Check.NotEmpty(modificationCommands, nameof(modificationCommands));
            var name = modificationCommands[0].TableName;
            var schema = modificationCommands[0].Schema;

            commandStringBuilder.AppendLine("EXECUTE BLOCK RETURNS (AffectedRows INT) AS BEGIN")
                                .AppendLine($"AffectedRows=0;");

            for (var i = 0; i < modificationCommands.Count; i++)
            {
                var operations = modificationCommands[i].ColumnModifications;
                var writeOperations = operations.Where(o => o.IsWrite).ToArray();
                var readOperations = operations.Where(o => o.IsRead).ToArray();
                commandStringBuilder.Append($"DELETE FROM {SqlGenerationHelper.DelimitIdentifier(name)} ");
                commandStringBuilder.AppendLine($" WHERE {SqlGenerationHelper.DelimitIdentifier(operations.First().ColumnName)}={operations[0].Value}; ");
                AppendUpdateOutputClause(commandStringBuilder);
            }
            commandStringBuilder.AppendLine("SUSPEND;");
            commandStringBuilder.AppendLine("END;");
            return ResultSetMapping.NotLastInResultSet;
        } 

        private void AppendUpdateOutputClause(StringBuilder commandStringBuilder)
        {
            //Increment of updates 
            commandStringBuilder
                    .AppendLine("IF (ROW_COUNT > 0) THEN")
                    .AppendLine("   AffectedRows=AffectedRows+ROW_COUNT;");

        } 

        private void AppendInsertOutputClause(
            StringBuilder commandStringBuilder,
            string name,
            string schema,
            IReadOnlyList<ColumnModification> operations,
            IReadOnlyList<ColumnModification> allOperations)
        {
            if (allOperations.Count > 0 && allOperations[0] == operations[0])
            {
                commandStringBuilder
                    .AppendLine($" RETURNING {SqlGenerationHelper.DelimitIdentifier(operations.First().ColumnName)} INTO :AffectedRows;")
                    .AppendLine("IF (ROW_COUNT > 0) THEN")
                    .AppendLine("   SUSPEND;");
            }
        }

        protected override ResultSetMapping AppendSelectAffectedCountCommand(StringBuilder commandStringBuilder, string name,
            string schema, int commandPosition)
        {
            // Not Implemented!
            return ResultSetMapping.LastInResultSet;
        }

        public override void AppendBatchHeader(StringBuilder commandStringBuilder)
        {
            //Insert FbSQL Fast(Insert/Update) 
        }

        protected override void AppendIdentityWhereCondition(StringBuilder commandStringBuilder, ColumnModification columnModification)
        {
            // Not Implemented!
        }

        protected override void AppendRowsAffectedWhereCondition(StringBuilder commandStringBuilder, int expectedRowsAffected)
        {
            // Not Implemented!
        }

        private string GetDataType(IProperty property)
        {
            var typeName = property.Fb().ColumnType;
            if (typeName == null)
            {
                var propertyDefault = property.FindPrincipal();
                typeName = propertyDefault?.Fb().ColumnType;
                if (typeName == null)
                {
                    if (property.ClrType == typeof(string))
                        typeName = _typeMapperRelational.StringMapper?.FindMapping(
                            property.IsUnicode() ?? propertyDefault?.IsUnicode() ?? true,
                            keyOrIndex: false,
                            maxLength: null).StoreType;
                    else if (property.ClrType == typeof(byte[]))
                        typeName = _typeMapperRelational.ByteArrayMapper?.FindMapping(rowVersion: false, keyOrIndex: false, size: null).StoreType;
                    else
                        typeName = _typeMapperRelational.FindMapping(property.ClrType).StoreType;
                }
            }
            if (property.ClrType == typeof(byte[]) && typeName != null)
                return property.IsNullable ? "varbinary(8)" : "binary(8)";

            return typeName;
        }

    }
}
