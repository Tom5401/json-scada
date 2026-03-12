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
using System.Linq;
using System.Threading.Tasks;
using MongoDB.Bson;
using MongoDB.Driver;

partial class MainClass
{
    // This process monitors and updates redundancy control of the driver instance in mongodb
    static async void ProcessRedundancyMongo()
    {
        do
        {
            try
            {
                var lastActiveNodeKeepAliveTimeTag = DateTime.MinValue;
                var countKeepAliveUpdates = 0;
                var countKeepAliveUpdatesLimit = 4;
                var Client = ConnectMongoClient(JSConfig);
                var DB = Client.GetDatabase(JSConfig.mongoDatabaseName);

                var collinsts =
                    DB.GetCollection<protocolDriverInstancesClass>(ProtocolDriverInstancesCollectionName);

                do
                {
                    var resStatus = DB.RunCommand((Command<BsonDocument>)"{ping:1}");
                    if (resStatus.Elements.Count() < 1 || resStatus.Elements.ElementAt(0).Value.AsDouble == 0)
                        throw new Exception("Error on MongoDB connection!");

                    var collconns =
                        DB.GetCollection<S7CP_connection>(ProtocolConnectionsCollectionName);

                    var instances =
                        collinsts
                            .Find(inst =>
                                inst.protocolDriver == ProtocolDriverName &&
                                inst.protocolDriverInstanceNumber == ProtocolDriverInstanceNumber)
                            .ToList();

                    var foundinstance = false;
                    foreach (protocolDriverInstancesClass inst in instances)
                    {
                        foundinstance = true;

                        var nodefound = false || inst.nodeNames.Length == 0;
                        foreach (var name in inst.nodeNames)
                        {
                            if (JSConfig.nodeName == name)
                            {
                                nodefound = true;
                            }
                        }
                        if (!nodefound)
                        {
                            Log("Node '" + JSConfig.nodeName + "' not found in instances configuration!");
                            Environment.Exit(-1);
                        }

                        if (inst.activeNodeName == JSConfig.nodeName)
                        {
                            if (!Active)
                                Log("Redundancy - ACTIVATING this Node!");
                            Active = true;
                            countKeepAliveUpdates = 0;
                        }
                        else
                        {
                            if (Active)
                            {
                                Log("Redundancy - DEACTIVATING this Node (other node active)!");
                                countKeepAliveUpdates = 0;
                                Random rnd = new Random();
                                await Task.Delay(rnd.Next(1000, 5000));
                            }
                            Active = false;
                            if (lastActiveNodeKeepAliveTimeTag == inst.activeNodeKeepAliveTimeTag)
                            {
                                countKeepAliveUpdates++;
                            }
                            lastActiveNodeKeepAliveTimeTag = inst.activeNodeKeepAliveTimeTag;
                            if (countKeepAliveUpdates > countKeepAliveUpdatesLimit)
                            {
                                Log("Redundancy - ACTIVATING this Node!");
                                Active = true;
                            }
                        }

                        if (Active)
                        {
                            Log("Redundancy - This node is active.");

                            // update keep alive time 
                            var filter1 =
                                Builders<protocolDriverInstancesClass>
                                    .Filter
                                    .Eq(m => m.protocolDriver, ProtocolDriverName);
                            var filter2 =
                                Builders<protocolDriverInstancesClass>
                                    .Filter
                                    .Eq(m => m.protocolDriverInstanceNumber, ProtocolDriverInstanceNumber);
                            var filter =
                                Builders<protocolDriverInstancesClass>
                                    .Filter
                                    .And(filter1, filter2);

                            var update =
                                Builders<protocolDriverInstancesClass>
                                    .Update
                                    .Set(m => m.activeNodeName, JSConfig.nodeName)
                                    .Set(m => m.activeNodeKeepAliveTimeTag, DateTime.Now);

                            var options =
                                new FindOneAndUpdateOptions<protocolDriverInstancesClass, protocolDriverInstancesClass>();
                            options.IsUpsert = false;
                            await collinsts.FindOneAndUpdateAsync(filter, update, options);

                            // update statistics for connections
                            foreach (S7CP_connection srv in S7CPconns)
                            {
                                if (srv.connection != null)
                                {
                                    var filt =
                                        new BsonDocument(new BsonDocument("protocolConnectionNumber",
                                            srv.protocolConnectionNumber));
                                    var upd =
                                        new BsonDocument("$set", new BsonDocument{
                                            {"stats", new BsonDocument{
                                                { "nodeName", JSConfig.nodeName },
                                                { "timeTag", BsonDateTime.Create(DateTime.Now) },
                                                }},
                                            });
                                    var res = collconns.UpdateOneAsync(filt, upd);
                                }
                            }
                        }
                        else
                        {
                            if (inst.activeNodeName != "")
                                Log("Redundancy - This node is INACTIVE! Node '" + inst.activeNodeName + "' is active, wait...");
                            else
                                Log("Redundancy - This node is INACTIVE! No node is active, wait...");
                        }

                        break; // process just first result
                    }

                    if (!foundinstance)
                    {
                        if (Active)
                        {
                            Log("Redundancy - DEACTIVATING this Node (no instance found)!");
                            countKeepAliveUpdates = 0;
                            Random rnd = new Random();
                            await Task.Delay(rnd.Next(1000, 5000));
                        }
                        Active = false;
                    }

                    await Task.Delay(5000);
                }
                while (true);
            }
            catch (Exception e)
            {
                Log("Exception Redundancy");
                Log(e);
                Log(e
                    .ToString()
                    .Substring(0,
                    e.ToString().IndexOf(Environment.NewLine)));
                await Task.Delay(3000);
            }
        }
        while (true);
    }
}
