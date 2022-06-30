﻿using System.Data;
using System.IO;
using Yuniql.Extensibility;
using MySql.Data.MySqlClient;
using Yuniql.Extensibility.BulkCsvParser;
using System;
using System.Diagnostics;
using System.Collections.Generic;

//https://github.com/22222/CsvTextFieldParser
namespace Yuniql.MySql
{
    ///<inheritdoc/>
    public class MySqlBulkImportService : IBulkImportService
    {
        private string _connectionString;
        private readonly ITraceService _traceService;

        ///<inheritdoc/>
        public MySqlBulkImportService(ITraceService traceService)
        {
            this._traceService = traceService;
        }

        ///<inheritdoc/>
        public void Initialize(string connectionString)
        {
            this._connectionString = connectionString;
        }

        ///<inheritdoc/>
        public void Run(
            IDbConnection connection,
            IDbTransaction transaction,
            string fileFullPath,
            string bulkSeparator = null,
            int? bulkBatchSize = null,
            int? commandTimeout = null,
            List<KeyValuePair<string, string>> tokens = null
        )
        {
            var connectionStringBuilder = new MySqlConnectionStringBuilder(_connectionString);

            //get file name segments from potentially sequenceno.schemaname.tablename filename pattern
            var fileName = Path.GetFileNameWithoutExtension(fileFullPath)
                          .ReplaceTokens(_traceService, tokens);
            var fileNameSegments = fileName.SplitBulkFileName(defaultSchema: connectionStringBuilder.Database);
            var schemaName = fileNameSegments.Item2;
            var tableName = fileNameSegments.Item3;

            if(!string.Equals(connectionStringBuilder.Database, schemaName, System.StringComparison.InvariantCultureIgnoreCase))
            {
                throw new ApplicationException("MySql does not support custom schema. Your bulk file name must resemble these patterns: 1.mytable.csv, 01.mytable.csv or mytable.csv");
            }

            var stopwatch = new Stopwatch();
            stopwatch.Start();
            _traceService.Info($"MySqlBulkImportService: Started copying data into destination table {schemaName}.{tableName}");

            //read csv file and load into data table
            var dataTable = ParseCsvFile(connection, fileFullPath, tableName, bulkSeparator);

            //save the csv data into staging sql table
            BulkCopyWithDataTable(connection, transaction, bulkBatchSize, tableName, dataTable);

            stopwatch.Stop();
            _traceService.Info($"MySqlBulkImportService: Finished copying data into destination table {schemaName}.{tableName} in {stopwatch.ElapsedMilliseconds} ms");
        }

        //TODO: Move into FileInfo.ParseCsvFile(fileFullPath, bulkSeparator)
        private DataTable ParseCsvFile(
            IDbConnection connection,
            string fileFullPath,
            string tableName,
            string bulkSeparator)
        {
            if (string.IsNullOrEmpty(bulkSeparator))
                bulkSeparator = ",";

            var csvDatatable = new DataTable();
            string query = $"SELECT * FROM {tableName} LIMIT 0;";
            using (var adapter = new MySqlDataAdapter(query, connection as MySqlConnection))
            {
                adapter.Fill(csvDatatable);
            };

            using (var csvReader = new CsvTextFieldParser(fileFullPath))
            {
                csvReader.Separators = (new string[] { bulkSeparator });

                //skipped the first row
                csvReader.ReadFields();

                //process data rows
                while (!csvReader.EndOfData)
                {
                    string[] fieldData = csvReader.ReadFields();
                    for (int i = 0; i < fieldData.Length; i++)
                    {
                        if (fieldData[i] == "" || fieldData[i] == "NULL")
                        {
                            fieldData[i] = null;
                        }
                    }
                    csvDatatable.Rows.Add(fieldData);
                }
            }
            return csvDatatable;
        }

        //https://dev.mysql.com/doc/connector-net/en/connector-net-programming-bulk-loader.html
        //https://stackoverflow.com/questions/48018614/insert-datatable-into-a-mysql-table-using-c-sharp

        //NOTE: This is not the most typesafe and performant way to do this and this is just to demonstrate
        //possibility to bulk import data in custom means during migration execution
        private void BulkCopyWithDataTable(
            IDbConnection connection,
            IDbTransaction transaction,
            int? bulkBatchSize,
            string tableName,
            DataTable dataTable)
        {
            using (var cmd = new MySqlCommand())
            {
                cmd.Connection = connection as MySqlConnection;
                cmd.Transaction = transaction as MySqlTransaction;
                cmd.CommandText = $"SELECT * FROM {tableName} LIMIT 0;";

                using (var adapter = new MySqlDataAdapter(cmd))
                {
                    adapter.UpdateBatchSize = bulkBatchSize.HasValue ? bulkBatchSize.Value : DEFAULT_CONSTANTS.BULK_BATCH_SIZE; ;
                    using (var cb = new MySqlCommandBuilder(adapter))
                    {
                        cb.SetAllValues = true;
                        adapter.Update(dataTable);
                    }
                };
            }
        }
    }
}
