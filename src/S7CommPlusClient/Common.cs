/* 
 * S7CommPlus Client Protocol driver for {json:scada}
 * {json:scada} - Copyright (c) 2020-2026 - Ricardo L. Olsen
 * S7CommPlusDriver - Copyright (C) 2023 Thomas Wiens, th.wiens@gmx.de
 * 
 * This file is part of the JSON-SCADA distribution (https://github.com/riclolsen/json-scada).
 * 
 * This program is free software: you can redistribute it and/or modify  
 * it under the terms of the GNU General Public License as published by  
 * the Free Software Foundation, version 3.
 */

using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Bson.Serialization.Serializers;
using MongoDB.Driver;

partial class MainClass
{
    // JSON-SCADA base configuration
    public class JSONSCADAConfig
    {
        public string nodeName { get; set; }
        public string mongoConnectionString { get; set; }
        public string mongoDatabaseName { get; set; }
        public string tlsCaPemFile { get; set; }
        public string tlsClientPemFile { get; set; }
        public string tlsClientPfxFile { get; set; }
        public string tlsClientKeyPassword { get; set; }
        public bool tlsAllowInvalidHostnames { get; set; }
        public bool tlsAllowChainErrors { get; set; }
        public bool tlsInsecure { get; set; }
    }

    // S7CommPlus connection configuration (from protocolConnections collection)
    [BsonIgnoreExtraElements]
    public class S7CP_connection
    {
        public ObjectId Id { get; set; }
        [BsonDefaultValue("")]
        public string protocolDriver { get; set; }
        [BsonDefaultValue(1)]
        public int protocolDriverInstanceNumber { get; set; }
        [BsonDefaultValue(1)]
        public int protocolConnectionNumber { get; set; }
        [BsonDefaultValue("NO NAME")]
        public string name { get; set; }
        [BsonDefaultValue("")]
        public string description { get; set; }
        [BsonDefaultValue(true)]
        public bool enabled { get; set; }
        [BsonDefaultValue(true)]
        public bool commandsEnabled { get; set; }
        public string[] endpointURLs { get; set; }
        [BsonDefaultValue(true)]
        public bool autoCreateTags { get; set; }
        [BsonDefaultValue(5000.0)]
        public double timeoutMs { get; set; }
        [BsonDefaultValue(1.0)]
        public double giInterval { get; set; }
        [BsonDefaultValue(false)]
        public bool useActiveTagRequests { get; set; }
        [BsonDefaultValue(5000)]
        public int activeTagRequestLimit { get; set; }
        [BsonDefaultValue("")]
        public string username { get; set; }
        [BsonDefaultValue("")]
        public string password { get; set; }
        [BsonDefaultValue(0.0)]
        public double hoursShift { get; set; }

        // Runtime state (not persisted in MongoDB)
        [BsonIgnore]
        public S7CommPlusDriver.S7CommPlusConnection connection;
        [BsonIgnore]
        public Thread connectionThread;
        [BsonIgnore]
        public bool isConnected = false;
        [BsonIgnore]
        public double LastNewKeyCreated = 0;
        [BsonIgnore]
        public SortedSet<string> InsertedAddresses = new SortedSet<string>();
        [BsonIgnore]
        public Dictionary<string, S7CommPlusDriver.ItemAddress> AddressCache = new Dictionary<string, S7CommPlusDriver.ItemAddress>();
        [BsonIgnore]
        public Dictionary<string, uint> SoftdatatypeCache = new Dictionary<string, uint>();
        [BsonIgnore]
        public Dictionary<uint, string> RelationIdNameMap = new Dictionary<uint, string>();
        [BsonIgnore]
        public Thread alarmThread;
        [BsonIgnore]
        public volatile bool alarmThreadStop = false;
    }

    public static string AlarmEventsCollectionName = "s7plusAlarmEvents";

    // Protocol driver instances configuration
    [BsonIgnoreExtraElements]
    public class protocolDriverInstancesClass
    {
        public ObjectId Id { get; set; }
        public int protocolDriverInstanceNumber { get; set; } = 1;
        public string protocolDriver { get; set; } = "";
        public bool enabled { get; set; } = true;
        public int logLevel { get; set; } = 1;
        public string[] nodeNames { get; set; } = Array.Empty<string>();
        public string activeNodeName { get; set; } = "";
        public DateTime activeNodeKeepAliveTimeTag { get; set; } = DateTime.MinValue;
        public bool keepProtocolRunningWhileInactive { get; set; } = false;
    }

    // Acquired data value to be enqueued for MongoDB update
    public class S7CPValue
    {
        public bool selfPublish;
        public bool createCommandForSupervised;
        public string address;
        public string asdu;
        public bool isArray;
        public double value;
        public string valueString;
        public string valueJson;
        public int cot;
        public DateTime serverTimestamp;
        public bool quality;
        public int conn_number;
        public string conn_name;
        public string display_name;
        public string path;
    }

    // Filter for MongoDB update queries
    public class rtFilt
    {
        public int protocolSourceConnectionNumber;
        public string protocolSourceObjectAddress;
        public string origin;
    }

    // Command from commandsQueue collection
    [BsonIgnoreExtraElements]
    public class rtCommand
    {
        public BsonObjectId id { get; set; }
        [BsonSerializer(typeof(BsonDoubleSerializer)), BsonDefaultValue(0)]
        public BsonDouble protocolSourceConnectionNumber { get; set; }
        [BsonSerializer(typeof(BsonDoubleSerializer)), BsonDefaultValue(0)]
        public BsonDouble protocolSourceCommonAddress { get; set; }
        [BsonDefaultValue("")]
        public BsonString protocolSourceObjectAddress { get; set; }
        [BsonDefaultValue("")]
        public BsonString protocolSourceASDU { get; set; }
        [BsonSerializer(typeof(BsonDoubleSerializer)), BsonDefaultValue(0)]
        public BsonDouble protocolSourceCommandDuration { get; set; }
        [BsonDefaultValue(false)]
        public BsonBoolean protocolSourceCommandUseSBO { get; set; }
        [BsonSerializer(typeof(BsonDoubleSerializer)), BsonDefaultValue(0)]
        public BsonDouble pointKey { get; set; }
        [BsonDefaultValue("")]
        public BsonString tag { get; set; }
        public BsonDateTime timeTag { get; set; }
        [BsonSerializer(typeof(BsonDoubleSerializer)), BsonDefaultValue(0)]
        public BsonDouble value { get; set; }
        [BsonDefaultValue("")]
        public BsonString valueString { get; set; }
        [BsonDefaultValue("")]
        public BsonString originatorUserName { get; set; }
        [BsonDefaultValue("")]
        public BsonString originatorIpAddress { get; set; }
        public BsonBoolean ack { get; set; }
        public BsonDateTime ackTimeTag { get; set; }
    }

    // Logging
    static void Log(string str, int level = 1)
    {
        if (LogLevel >= level)
        {
            var now = DateTime.Now;
            LogMutex.WaitOne();
            Console.Write($"[{now:o}]");
            Console.WriteLine(" " + str);
            LogMutex.ReleaseMutex();
        }
    }

    static void Log(Exception e, int level = 1)
    {
        Log(e.ToString(), level);
    }

    // BsonDouble permissive deserializer
    public class BsonDoubleSerializer : SerializerBase<BsonDouble>
    {
        public override void Serialize(BsonSerializationContext context, BsonSerializationArgs args, BsonDouble dval)
        {
            context.Writer.WriteDouble(dval.ToDouble());
        }
        public override BsonDouble Deserialize(BsonDeserializationContext context, BsonDeserializationArgs args)
        {
            var type = context.Reader.GetCurrentBsonType();
            double dval = 0.0;
            switch (type)
            {
                case BsonType.Double:
                    return context.Reader.ReadDouble();
                case BsonType.Null:
                    context.Reader.ReadNull();
                    break;
                case BsonType.String:
                    try { dval = double.Parse(context.Reader.ReadString()); } catch { }
                    break;
                case BsonType.ObjectId:
                    try { dval = double.Parse(context.Reader.ReadObjectId().ToString()); } catch { }
                    break;
                case BsonType.JavaScript:
                    try { dval = double.Parse(context.Reader.ReadJavaScript()); } catch { }
                    break;
                case BsonType.Decimal128:
                    dval = Convert.ToDouble(context.Reader.ReadDecimal128());
                    break;
                case BsonType.Boolean:
                    dval = Convert.ToDouble(context.Reader.ReadBoolean());
                    break;
                case BsonType.Int32:
                    dval = context.Reader.ReadInt32();
                    break;
                case BsonType.Int64:
                    dval = context.Reader.ReadInt64();
                    break;
            }
            return dval;
        }
    }

    // MongoDB client connection
    static MongoClient ConnectMongoClient(JSONSCADAConfig jsConfig)
    {
        MongoClientSettings settings = MongoClientSettings.FromUrl(new MongoUrl(jsConfig.mongoConnectionString));
        if (!string.IsNullOrEmpty(jsConfig.tlsClientPfxFile))
        {
            var pem = File.ReadAllText(jsConfig.tlsCaPemFile);
            byte[] certBuffer = GetBytesFromPEM(pem, "CERTIFICATE");
            var caCert = new X509Certificate2(certBuffer);
            var cliCert = new X509Certificate2(jsConfig.tlsClientPfxFile, jsConfig.tlsClientKeyPassword);
            settings.UseTls = true;
            settings.AllowInsecureTls = true;
            settings.SslSettings = new SslSettings
            {
                ClientCertificates = new[] { caCert, cliCert },
                CheckCertificateRevocation = false,
                ServerCertificateValidationCallback = CertificateValidationCallBack
            };
        }
        return new MongoClient(settings);
    }

    static void EnsureActiveTagRequestIndexes(IMongoDatabase db)
    {
        try
        {
            var collection = db.GetCollection<BsonDocument>(ActiveTagRequestsCollectionName);
            collection.Indexes.CreateOne(new CreateIndexModel<BsonDocument>(
                Builders<BsonDocument>.IndexKeys.Ascending("expiresAt"),
                new CreateIndexOptions { ExpireAfter = TimeSpan.Zero, Name = "ttl_expiresAt" }));
            collection.Indexes.CreateOne(new CreateIndexModel<BsonDocument>(
                Builders<BsonDocument>.IndexKeys
                    .Ascending("protocolSourceConnectionNumber")
                    .Ascending("protocolSourceObjectAddress"),
                new CreateIndexOptions { Unique = true, Name = "uniq_conn_addr" }));
        }
        catch (Exception e)
        {
            Log("Failed to ensure active tag request indexes: " + e.Message, LogLevelDetailed);
        }
    }

    static List<string> GetActiveAddressesForConnection(S7CP_connection srv)
    {
        if (MongoDatabase == null)
            return null;

        try
        {
            var collection = MongoDatabase.GetCollection<BsonDocument>(ActiveTagRequestsCollectionName);
            var filter = Builders<BsonDocument>.Filter.And(
                Builders<BsonDocument>.Filter.Eq("protocolSourceConnectionNumber", (double)srv.protocolConnectionNumber),
                Builders<BsonDocument>.Filter.Gt("expiresAt", DateTime.UtcNow));
            var projection = Builders<BsonDocument>.Projection
                .Include("protocolSourceObjectAddress")
                .Exclude("_id");
            int requestLimit = Math.Max(1, srv.activeTagRequestLimit);

            var docs = collection.Find(filter)
                .Limit(requestLimit)
                .Project<BsonDocument>(projection)
                .ToList();

            var addresses = new List<string>(docs.Count);
            foreach (var doc in docs)
            {
                if (doc.TryGetValue("protocolSourceObjectAddress", out var value) && value.IsString)
                    addresses.Add(value.AsString);
            }

            return addresses;
        }
        catch (Exception e)
        {
            Log(srv.name + " - Failed to query active tag requests: " + e.Message, LogLevelDetailed);
            Log(e, LogLevelDetailed);
            return null;
        }
    }

    static byte[] GetBytesFromPEM(string pemString, string section)
    {
        var header = $"-----BEGIN {section}-----";
        var footer = $"-----END {section}-----";
        var start = pemString.IndexOf(header, StringComparison.Ordinal) + header.Length;
        var end = pemString.IndexOf(footer, start, StringComparison.Ordinal);
        var base64 = pemString.Substring(start, end - start).Trim();
        return Convert.FromBase64String(base64);
    }

    static bool CertificateValidationCallBack(
        object sender,
        X509Certificate certificate,
        X509Chain chain,
        SslPolicyErrors sslPolicyErrors)
    {
        return true; // Accept all certificates (configurable in production)
    }
}
