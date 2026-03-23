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
 *
 * This program is distributed in the hope that it will be useful, but 
 * WITHOUT ANY WARRANTY; without even the implied warranty of 
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the GNU 
 * General Public License for more details.
 *
 * You should have received a copy of the GNU General Public License 
 * along with this program. If not, see <http://www.gnu.org/licenses/>.
 */

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Bson.Serialization.Serializers;
using MongoDB.Driver;

partial class MainClass
{
    public static string CopyrightMessage = "{json:scada} S7CommPlus Client Driver - Copyright 2026";
    public static string ProtocolDriverName = "S7COMMPLUS";
    public static string DriverVersion = "0.1.0";
    public static bool Active = false;
    public static uint DataBufferLimit = 50000;
    public static uint BulkWriteLimit = 6000;

    public static string JsonConfigFilePath = @"../conf/json-scada.json";
    public static string JsonConfigFilePathAlt = @"c:/json-scada/conf/json-scada.json";

    public static int LogLevelNoLog = 0;
    public static int LogLevelBasic = 1;
    public static int LogLevelDetailed = 2;
    public static int LogLevelDebug = 3;
    public static int LogLevel = 1;
    private static Mutex LogMutex = new Mutex();

    public static JSONSCADAConfig JSConfig;
    public static protocolDriverInstancesClass DriverInstance = null;

    public static string ProtocolConnectionsCollectionName = "protocolConnections";
    public static string ProtocolDriverInstancesCollectionName = "protocolDriverInstances";
    public static string RealtimeDataCollectionName = "realtimeData";
    public static string SoeDataCollectionName = "soeData";
    public static string CommandsQueueCollectionName = "commandsQueue";
    public static string ActiveTagRequestsCollectionName = "activeTagRequests";
    public static int ProtocolDriverInstanceNumber = 1;
    public static MongoClient MongoClientInstance;
    public static IMongoDatabase MongoDatabase;

    public static ConcurrentQueue<S7CPValue> DataQueue = new ConcurrentQueue<S7CPValue>();
    public static List<S7CP_connection> S7CPconns = new List<S7CP_connection>();

    public static int Main(string[] args)
    {
        Log(CopyrightMessage);
        Log("Driver version " + DriverVersion);

        if (args.Length > 0)
        {
            if (int.TryParse(args[0], out int num))
                ProtocolDriverInstanceNumber = num;
        }
        if (args.Length > 1)
        {
            if (int.TryParse(args[1], out int num))
                LogLevel = num;
        }

        string fname = JsonConfigFilePath;
        if (args.Length > 2)
        {
            if (File.Exists(args[2]))
                fname = args[2];
        }
        if (!File.Exists(fname))
            fname = JsonConfigFilePathAlt;
        if (!File.Exists(fname))
        {
            Log("Missing config file " + JsonConfigFilePath);
            Environment.Exit(-1);
        }

        Log("Reading config file " + fname);
        string json = File.ReadAllText(fname);
        JSConfig = JsonSerializer.Deserialize<JSONSCADAConfig>(json);
        if (string.IsNullOrEmpty(JSConfig.mongoConnectionString))
        {
            Log("Missing MongoDB connection string in JSON config file " + fname);
            Environment.Exit(-1);
        }
        if (string.IsNullOrEmpty(JSConfig.mongoDatabaseName))
        {
            Log("Missing MongoDB database name in JSON config file " + fname);
            Environment.Exit(-1);
        }
        Log("MongoDB database name: " + JSConfig.mongoDatabaseName);
        if (string.IsNullOrEmpty(JSConfig.nodeName))
        {
            Log("Missing nodeName parameter in JSON config file " + fname);
            Environment.Exit(-1);
        }
        Log("Node name: " + JSConfig.nodeName);

        var Client = ConnectMongoClient(JSConfig);
        var DB = Client.GetDatabase(JSConfig.mongoDatabaseName);
        MongoClientInstance = Client;
        MongoDatabase = DB;
        EnsureActiveTagRequestIndexes(DB);

        // read and process instances configuration
        var collinsts = DB.GetCollection<protocolDriverInstancesClass>(ProtocolDriverInstancesCollectionName);
        var instances = collinsts
            .Find(inst =>
                inst.protocolDriver == ProtocolDriverName &&
                inst.protocolDriverInstanceNumber == ProtocolDriverInstanceNumber &&
                inst.enabled == true)
            .ToList();

        var foundInstance = false;
        foreach (protocolDriverInstancesClass inst in instances)
        {
            if (ProtocolDriverName == inst.protocolDriver &&
                ProtocolDriverInstanceNumber == inst.protocolDriverInstanceNumber)
            {
                foundInstance = true;
                if (!inst.enabled)
                {
                    Log("Driver instance [" + ProtocolDriverInstanceNumber + "] disabled!");
                    Environment.Exit(-1);
                }
                Log("Instance: " + inst.protocolDriverInstanceNumber);
                var nodefound = false || inst.nodeNames.Length == 0;
                foreach (var name in inst.nodeNames)
                {
                    if (JSConfig.nodeName == name)
                        nodefound = true;
                }
                if (!nodefound)
                {
                    Log("Node '" + JSConfig.nodeName + "' not found in instances configuration!");
                    Environment.Exit(-1);
                }
                DriverInstance = inst;
                break;
            }
            break;
        }
        if (!foundInstance)
        {
            Log("Driver instance [" + ProtocolDriverInstanceNumber + "] not found in configuration!");
            Environment.Exit(-1);
        }

        // read and process connections configuration for this driver instance
        var collconns = DB.GetCollection<S7CP_connection>(ProtocolConnectionsCollectionName);
        var conns = collconns
            .Find(conn =>
                conn.protocolDriver == ProtocolDriverName &&
                conn.protocolDriverInstanceNumber == ProtocolDriverInstanceNumber &&
                conn.enabled == true)
            .ToList();

        var collRtData = DB.GetCollection<rtData>(RealtimeDataCollectionName);

        foreach (S7CP_connection isrv in conns)
        {
            var results = collRtData.Find<rtData>(new BsonDocument {
                { "protocolSourceConnectionNumber", isrv.protocolConnectionNumber },
            }).ToList();
            Log(isrv.name + " - Found " + results.Count + " tags in database.");

            for (int i = 0; i < results.Count; i++)
            {
                isrv.InsertedAddresses.Add(results[i].protocolSourceObjectAddress);
            }

            isrv.LastNewKeyCreated = 0;
            if (isrv.endpointURLs == null || isrv.endpointURLs.Length < 1)
            {
                Log("Missing remote endpoint URLs list for connection " + isrv.name + "!");
                continue;
            }
            S7CPconns.Add(isrv);
            Log(isrv.name + " - New Connection");
        }
        if (S7CPconns.Count == 0)
        {
            Log("No connections found!");
            Environment.Exit(-1);
        }

        // start redundancy control thread
        var thrRedundancy = new Thread(() => ProcessRedundancyMongo());
        thrRedundancy.Start();

        // start MongoDB update task
        _ = Task.Run(ProcessMongo);

        // start command handling thread
        var thrMongoCmd = new Thread(() => ProcessMongoCmd());
        thrMongoCmd.Start();

        // start S7CommPlus connection threads
        Log("Setting up S7CommPlus connections...");
        foreach (S7CP_connection srv in S7CPconns)
        {
            srv.connectionThread = new Thread(() => ConnectionThread(srv));
            srv.connectionThread.Start();
        }

        Thread.Sleep(1000);

        // main loop - monitor connection threads
        do
        {
            Thread.Sleep(2000);

            if (!Console.IsInputRedirected)
                if (Console.KeyAvailable)
                {
                    if (Console.ReadKey().Key == ConsoleKey.Escape)
                    {
                        Log("Exiting application!");
                        Environment.Exit(0);
                    }
                    else
                        Log("Press 'Esc' key to terminate...");
                }
        } while (true);
    }

    static void ConnectionThread(S7CP_connection srv)
    {
        do
        {
            try
            {
                if (!Active)
                {
                    Thread.Sleep(2000);
                    continue;
                }

                if (srv.connection == null || !srv.isConnected)
                {
                    Log(srv.name + " - Connecting to " + srv.endpointURLs[0] + "...");
                    srv.connection = new S7CommPlusDriver.S7CommPlusConnection();
                    int res = srv.connection.Connect(
                        srv.endpointURLs[0],
                        srv.password,
                        srv.username,
                        (int)srv.timeoutMs);

                    if (res != 0)
                    {
                        Log(srv.name + " - Connection failed! Error: " + res);
                        srv.isConnected = false;
                        srv.connection = null;
                        Thread.Sleep(5000);
                        continue;
                    }

                    srv.isConnected = true;
                    Log(srv.name + " - Connected successfully.");

                    // Build RelationId-to-name map from PLC datablock browse
                    {
                        List<S7CommPlusDriver.S7CommPlusConnection.DatablockInfo> dbInfoList;
                        int browseRes = srv.connection.GetListOfDatablocks(out dbInfoList);
                        if (browseRes != 0)
                        {
                            Log(srv.name + " - GetListOfDatablocks failed (error: " + browseRes + "); originDbName will be empty.", LogLevelBasic);
                            srv.RelationIdNameMap = new Dictionary<uint, string>();
                        }
                        else
                        {
                            var map = new Dictionary<uint, string>(dbInfoList.Count);
                            foreach (var db in dbInfoList)
                                map[db.db_block_relid] = db.db_name;
                            srv.RelationIdNameMap = map;
                            Log(srv.name + " - RelationIdNameMap built: " + map.Count + " datablocks.", LogLevelDetailed);
                        }
                    }

                    // Start alarm subscription thread (separate connection to PLC)
                    srv.alarmThreadStop = false;
                    srv.alarmThread = new Thread(() => AlarmThread(srv));
                    srv.alarmThread.Start();
                    Log(srv.name + " - AlarmThread started.");

                    // Browse if autoCreateTags enabled
                    if (srv.autoCreateTags)
                    {
                        Log(srv.name + " - Browsing PLC tags...");
                        BrowseAndCreateTags(srv);
                    }
                }

                // Read cycle
                if (srv.isConnected && Active)
                {
                    PerformReadCycle(srv);
                }

                // Wait for next cycle
                Thread.Sleep(Math.Max(100, (int)(srv.giInterval * 1000)));
            }
            catch (Exception e)
            {
                Log(srv.name + " - Exception in connection thread: " + e.Message);
                Log(e, LogLevelDetailed);
                // Stop alarm thread before resetting connection
                if (srv.alarmThread != null)
                {
                    srv.alarmThreadStop = true;
                    srv.alarmThread.Join(3000);
                    srv.alarmThread = null;
                    Log(srv.name + " - AlarmThread stopped.");
                }
                srv.isConnected = false;
                srv.connection = null;
                Thread.Sleep(5000);
            }
        } while (true);
    }
}
