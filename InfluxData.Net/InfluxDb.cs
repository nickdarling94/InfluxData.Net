﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using InfluxData.Net.Contracts;
using InfluxData.Net.Enums;
using InfluxData.Net.Helpers;
using InfluxData.Net.Infrastructure.Configuration;
using InfluxData.Net.Infrastructure.Influx;
using InfluxData.Net.Infrastructure.Validation;
using InfluxData.Net.Models;
using InfluxData.Net.Models.Responses;
using InfluxData.Net.Infrastructure.Clients;

namespace InfluxData.Net
{
    public class InfluxDb : IInfluxDb
    {
        internal readonly IEnumerable<ApiResponseErrorHandlingDelegate> NoErrorHandlers = Enumerable.Empty<ApiResponseErrorHandlingDelegate>();

        private readonly IInfluxDbClient _influxDbClient;

        public InfluxDb(string url, string username, string password, InfluxVersion influxVersion)
             : this(new InfluxDbClientConfiguration(new Uri(url), username, password, influxVersion))
        {
            Validate.NotNullOrEmpty(url, "The URL may not be null or empty.");
            Validate.NotNullOrEmpty(username, "The username may not be null or empty.");
        }

        internal InfluxDb(InfluxDbClientConfiguration influxDbClientConfiguration)
        {
            switch (influxDbClientConfiguration.InfluxVersion)
            {
                case InfluxVersion.Latest:
                    _influxDbClient = new InfluxDbClientV09x(influxDbClientConfiguration);
                    break;
                case InfluxVersion.v096:
                    _influxDbClient = new InfluxDbClientV096(influxDbClientConfiguration);
                    break;
                case InfluxVersion.v095:
                    _influxDbClient = new InfluxDbClientV095(influxDbClientConfiguration);
                    break;
                case InfluxVersion.v092:
                    _influxDbClient = new InfluxDbClientV092(influxDbClientConfiguration);
                    break;
                case InfluxVersion.v08x:
                    throw new NotImplementedException("InfluxDB v0.8.x is not supported by InfluxData.Net library.");
                default:
                    throw new ArgumentOutOfRangeException("influxDbClientConfiguration", String.Format("Unknown version {0}.", influxDbClientConfiguration));
            }
        }

        #region Base

        public IFormatter GetFormatter()
        {
            return _influxDbClient.GetFormatter();
        }

        public InfluxVersion GetClientVersion()
        {
            return _influxDbClient.GetVersion();
        }

        #endregion Base

        #region Database

        public async Task<InfluxDbApiResponse> CreateDatabaseAsync(string dbName)
        {
            return await _influxDbClient.CreateDatabase(dbName);
        }

        public async Task<InfluxDbApiResponse> DropDatabaseAsync(string dbName)
        {
            return await _influxDbClient.DropDatabase(dbName);
        }

        public async Task<List<DatabaseResponse>> ShowDatabasesAsync()
        {
            var response = await _influxDbClient.ShowDatabases();
            var queryResult = response.ReadAs<QueryResponse>();
            var serie = queryResult.Results.Single().Series.Single();
            var databases = new List<DatabaseResponse>();

            foreach (var value in serie.Values)
            {
                databases.Add(new DatabaseResponse
                {
                    Name = (string)value[0]
                });
            }

            return databases;
        }

        public async Task<InfluxDbApiResponse> DropSeriesAsync(string dbName, string serieName)
        {
            return await _influxDbClient.DropSeries(dbName, serieName);
        }

        #endregion Database

        #region Basic Querying

        public async Task<InfluxDbApiWriteResponse> WriteAsync(string dbName, Point point, string retenionPolicy = "default")
        {
            return await WriteAsync(dbName, new[] { point }, retenionPolicy);
        }

        public async Task<InfluxDbApiWriteResponse> WriteAsync(string dbName, Point[] points, string retenionPolicy = "default")
        {
            var request = new WriteRequest(_influxDbClient.GetFormatter())
            {
                Database = dbName,
                Points = points,
                RetentionPolicy = retenionPolicy
            };

            // TODO: handle precision (if set by client, it makes no difference because it gets overriden here)
            var result = await _influxDbClient.Write(request, TimeUnitUtility.ToTimePrecision(TimeUnit.Milliseconds));

            return result;
        }

        public async Task<List<Serie>> QueryAsync(string dbName, string query)
        {
            InfluxDbApiResponse response = await _influxDbClient.Query(dbName, query);
            var queryResult = response.ReadAs<QueryResponse>();

            Validate.NotNull(queryResult, "queryResult");
            Validate.NotNull(queryResult.Results, "queryResult.Results");

            // Apparently a 200 OK can return an error in the results
            // https://github.com/influxdb/influxdb/pull/1813
            var error = queryResult.Results.Single().Error;
            if (error != null)
            {
                throw new InfluxDbApiException(System.Net.HttpStatusCode.BadRequest, error);
            }

            var result = queryResult.Results.Single().Series;

            return result != null ? result.ToList() : new List<Serie>();
        }
        
        #endregion Basic Querying

        #region Continuous Queries

        public async Task<InfluxDbApiResponse> CreateContinuousQueryAsync(ContinuousQuery cqRequest)
        {
            return await _influxDbClient.CreateContinuousQuery(cqRequest);
        }

        public async Task<Serie> GetContinuousQueriesAsync(string dbName)
        {
            InfluxDbApiResponse response = await _influxDbClient.GetContinuousQueries(dbName);
            var queryResult = response.ReadAs<QueryResponse>();//.Results.Single().Series;

            Validate.NotNull(queryResult, "queryResult");
            Validate.NotNull(queryResult.Results, "queryResult.Results");

            // Apparently a 200 OK can return an error in the results
            // https://github.com/influxdb/influxdb/pull/1813
            var error = queryResult.Results.Single().Error;
            if (error != null)
            {
                throw new InfluxDbApiException(System.Net.HttpStatusCode.BadRequest, error);
            }

            var series = queryResult.Results.Single().Series;

            return series != null ? series.Where(p => p.Name == dbName).FirstOrDefault() : new Serie();
        }

        public async Task<InfluxDbApiResponse> DeleteContinuousQueryAsync(string dbName, string cqName)
        {
            return await _influxDbClient.DeleteContinuousQuery(dbName, cqName);
        }

        #endregion Continuous Queries

        #region Other

        public async Task<Pong> PingAsync()
        {
            var watch = Stopwatch.StartNew();

            var response = await _influxDbClient.Ping();

            watch.Stop();

            return new Pong
            {
                Version = response.Body,
                ResponseTime = watch.Elapsed,
                Success = true
            };
        }

        #endregion Othe
    }
}