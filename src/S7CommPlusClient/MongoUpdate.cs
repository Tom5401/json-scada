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
using System.Diagnostics;
using System.Threading.Tasks;
using MongoDB.Bson;
using MongoDB.Bson.IO;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;
using MongoDB.Driver;

partial class MainClass
{
    public static int AutoKeyMultiplier = 1000000; // maximum number of points on each connection self-published (auto numbered points)

    // This process updates acquired values in the mongodb collection for realtime data
    static public async Task ProcessMongo()
    {
        do
        {
            try
            {
                var serializer = new BsonValueSerializer();
                var Client = ConnectMongoClient(JSConfig);
                var DB = Client.GetDatabase(JSConfig.mongoDatabaseName);
                var collection =
                    DB.GetCollection<rtData>(RealtimeDataCollectionName);
                var collectionId =
                    DB.GetCollection<rtDataId>(RealtimeDataCollectionName);
                var listWrites = new List<WriteModel<rtData>>();
                var filt = new rtFilt
                {
                    protocolSourceConnectionNumber = 0,
                    protocolSourceObjectAddress = "",
                    origin = "supervised"
                };

                Log("MongoDB Update Thread Started...");

                do
                {
                    Stopwatch stopWatch = new Stopwatch();
                    stopWatch.Start();

                    while (DataQueue.TryDequeue(result: out var ov))
                    {
                        BsonValue bsontt = BsonNull.Value;
                        try
                        {
                            bsontt = BsonValue.Create(ov.serverTimestamp);
                        }
                        catch
                        {
                            bsontt = BsonNull.Value;
                        }

                        BsonValue valBSON = BsonNull.Value;
                        if (!string.IsNullOrEmpty(ov.valueJson))
                        {
                            try
                            {
                                valBSON = serializer.Deserialize(BsonDeserializationContext.CreateRoot(new JsonReader(ov.valueJson)));
                            }
                            catch (Exception e)
                            {
                                Log(ov.conn_name + " - " + e.Message);
                            }
                        }

                        if (ov.selfPublish)
                        {
                            // find the json-scada connection for this received value 
                            int conn_index = 0;
                            for (int index = 0; index < S7CPconns.Count; index++)
                            {
                                if (S7CPconns[index].protocolConnectionNumber == ov.conn_number)
                                {
                                    conn_index = index;
                                    break;
                                }
                            }

                            string tag = ov.conn_name + ";" + ov.address;
                            if (S7CPconns[conn_index].InsertedAddresses.Add(ov.address))
                            { // added, then insert it
                                Log(ov.conn_name + " - INSERT NEW TAG: " + tag + " - Addr:" + ov.address, LogLevelDetailed);

                                // find a new free _id key based on the connection number
                                if (S7CPconns[conn_index].LastNewKeyCreated == 0)
                                {
                                    double AutoKeyId = (double)ov.conn_number * AutoKeyMultiplier;
                                    var results = collectionId.Find<rtDataId>(new BsonDocument {
                                            { "_id", new BsonDocument{
                                                { "$gt", AutoKeyId },
                                                { "$lt", (double)(ov.conn_number + 1) * AutoKeyMultiplier }
                                                }
                                            }
                                            }).Sort(Builders<rtDataId>.Sort.Descending("_id"))
                                        .Limit(1)
                                        .ToList();

                                    if (results.Count > 0)
                                    {
                                        S7CPconns[conn_index].LastNewKeyCreated = results[0]._id.ToDouble() + 1;
                                    }
                                    else
                                    {
                                        S7CPconns[conn_index].LastNewKeyCreated = AutoKeyId;
                                    }
                                }
                                else
                                    S7CPconns[conn_index].LastNewKeyCreated = S7CPconns[conn_index].LastNewKeyCreated + 1;

                                // will create a new command tag when the variable is writable
                                var commandOfSupervised = 0.0;
                                if (S7CPconns[conn_index].commandsEnabled && ov.createCommandForSupervised)
                                {
                                    var insert_ = newRealtimeDoc(ov, S7CPconns[conn_index].LastNewKeyCreated, commandOfSupervised);
                                    listWrites.Add(new InsertOneModel<rtData>(insert_));

                                    commandOfSupervised = S7CPconns[conn_index].LastNewKeyCreated;
                                    S7CPconns[conn_index].LastNewKeyCreated++;
                                }

                                ov.createCommandForSupervised = false;
                                var insert = newRealtimeDoc(ov, S7CPconns[conn_index].LastNewKeyCreated, commandOfSupervised);
                                listWrites.Add(new InsertOneModel<rtData>(insert));

                                // will immediately be followed by an update below (to the same tag)
                            }
                        }

                        // update one existing document with received tag value (realtimeData)
                        var update =
                            new BsonDocument {
                                {
                                    "$set",
                                    new BsonDocument {
                                        {
                                            "sourceDataUpdate",
                                            new BsonDocument {
                                                { "valueBsonAtSource", valBSON },
                                                { "valueJsonAtSource", ov.valueJson ?? "" },
                                                { "valueAtSource", BsonDouble.Create(ov.value) },
                                                { "valueStringAtSource", BsonString.Create(ov.valueString ?? "") },
                                                { "asduAtSource", BsonString.Create(ov.asdu ?? "") },
                                                { "causeOfTransmissionAtSource", BsonString.Create(ov.cot.ToString()) },
                                                { "timeTagAtSource", bsontt },
                                                { "timeTagAtSourceOk", BsonBoolean.Create(true) },
                                                { "timeTag", BsonValue.Create(ov.serverTimestamp) },
                                                { "notTopicalAtSource", BsonBoolean.Create(false) },
                                                { "invalidAtSource", BsonBoolean.Create(!ov.quality) },
                                                { "overflowAtSource", BsonBoolean.Create(false) },
                                                { "blockedAtSource", BsonBoolean.Create(false) },
                                                { "substitutedAtSource", BsonBoolean.Create(false) }
                                            }
                                        }
                                    }
                                }
                            };

                        // update filter, avoids updating commands that can have the same address as supervised points
                        filt.protocolSourceConnectionNumber = ov.conn_number;
                        filt.protocolSourceObjectAddress = ov.address;
                        Log("MongoDB - ADD " + ov.address + " " + ov.value, LogLevelDebug);

                        var tooBig = false;
                        if ((ov.valueJson ?? "").Length + (ov.valueString ?? "").Length > 1000000 && update.ToBson().Length > 16000000)
                        {
                            Log("MongoDB - Too big update for " + ov.address + " - " + update.ToBson().Length + " bytes, will not be written to MongoDB", LogLevelDetailed);
                            tooBig = true;
                        }
                        if (!tooBig)
                            listWrites
                                .Add(new UpdateOneModel<rtData>(
                                    filt.ToBsonDocument(),
                                    update));

                        if (listWrites.Count >= BulkWriteLimit)
                            break;

                        if (stopWatch.ElapsedMilliseconds > 750)
                        {
                            Log($"break ms {stopWatch.ElapsedMilliseconds}");
                            break;
                        }
                    }

                    if (listWrites.Count > 0)
                    {
                        stopWatch.Restart();
                        Log("MongoDB - Bulk writing " + listWrites.Count + ", Total enqueued data " + DataQueue.Count);
                        try
                        {
                            var bulkWriteResult = await collection
                                .WithWriteConcern(WriteConcern.Unacknowledged)
                                .BulkWriteAsync(listWrites, new BulkWriteOptions
                                {
                                    IsOrdered = false,
                                    BypassDocumentValidation = true,
                                }).ConfigureAwait(false);

                            var ups = (uint)((float)listWrites.Count / ((float)stopWatch.ElapsedMilliseconds / 1000));
                            Log($"MongoDB - Bulk written {listWrites.Count} documents in {stopWatch.ElapsedMilliseconds} ms, updates per second: {ups}");
                            listWrites.Clear();
                        }
                        catch (Exception e)
                        {
                            Log($"MongoDB - Bulk write error - " + e.Message);
                        }
                    }

                    if (DataQueue.Count == 0)
                    {
                        await Task.Delay(200).ConfigureAwait(false);
                    }
                }
                while (true);
            }
            catch (Exception e)
            {
                Log("Exception Mongo");
                Log(e);
                Log(e
                    .ToString()
                    .Substring(0,
                    e.ToString().IndexOf(Environment.NewLine)));
                await Task.Delay(1000).ConfigureAwait(false);
            }
        }
        while (true);
    }
}
