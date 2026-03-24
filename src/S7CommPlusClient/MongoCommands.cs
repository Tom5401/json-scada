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
using System.Threading;
using MongoDB.Bson;
using MongoDB.Driver;
using S7CommPlusDriver;

partial class MainClass
{
    // This process watches (via change stream) for commands inserted to a commands collection
    // When the command is considered valid it is forwarded to the PLC
    static async void ProcessMongoCmd()
    {
        do
        {
            try
            {
                var Client = ConnectMongoClient(JSConfig);
                var DB = Client.GetDatabase(JSConfig.mongoDatabaseName);
                var collection = DB.GetCollection<rtCommand>(CommandsQueueCollectionName);

                BsonDocument filter, update;

                bool isMongoLive = DB.RunCommandAsync((Command<BsonDocument>)"{ping:1}").Wait(1000);
                if (!isMongoLive) throw new Exception("Error on connection " + JSConfig.mongoConnectionString);

                Log("MongoDB CMD CS - Start listening for commands via changestream...");

                var pipeline = new EmptyPipelineDefinition<ChangeStreamDocument<rtCommand>>().Match("{ operationType: 'insert' }");

                using (var cursor = await collection.WatchAsync(pipeline))
                {
                    await cursor.ForEachAsync(async change =>
                    {
                        if (!Active) return;

                        // process change event, only process inserts
                        if (change.OperationType != ChangeStreamOperationType.Insert) return;

                        Log("MongoDB CMD CS - Looking for connection " +
                            change.FullDocument.protocolSourceConnectionNumber +
                            "...", LogLevelDetailed);

                        foreach (S7CP_connection srv in S7CPconns)
                        {
                            if (srv.protocolConnectionNumber != change.FullDocument.protocolSourceConnectionNumber)
                                continue;

                            // test for command expired
                            int timeDif = DateTime.Now.ToLocalTime()
                                    .Subtract(change.FullDocument.timeTag.ToLocalTime()).Seconds;
                            if (timeDif > 10)
                            {
                                // update as expired
                                Log("MongoDB CMD CS - " + srv.name + " - Address " +
                                    change.FullDocument.protocolSourceObjectAddress +
                                    " value " + change.FullDocument.value +
                                    " Command Timeout Expired, " + timeDif + " Seconds old");
                                filter = new BsonDocument(new BsonDocument("_id", change.FullDocument.id));
                                update = new BsonDocument("$set", new BsonDocument("cancelReason", "expired"));
                                await collection.UpdateOneAsync(filter, update);
                                break;
                            }

                            if (!srv.isConnected || srv.connection == null || !srv.commandsEnabled)
                            {
                                // update as canceled (not connected)
                                Log("MongoDB CMD CS - " +
                                    srv.name + " Address " + change.FullDocument.protocolSourceObjectAddress +
                                    " value " + change.FullDocument.value +
                                    (srv.commandsEnabled ? " Not Connected" : " Commands Disabled"));
                                filter = new BsonDocument(new BsonDocument("_id", change.FullDocument.id));
                                update = new BsonDocument("$set", new BsonDocument("cancelReason",
                                            srv.commandsEnabled ? "not connected" : "commands disabled"));
                                await collection.UpdateOneAsync(filter, update);
                                break;
                            }

                            string address = change.FullDocument.protocolSourceObjectAddress.ToString();
                            string asdu = change.FullDocument.protocolSourceASDU.ToString();
                            double cmdValue = change.FullDocument.value;
                            string cmdValueString = change.FullDocument.valueString.ToString();

<<<<<<< HEAD
                            // Handle alarm acknowledgment (sent by admin UI with ASDU "s7plus-alarm-ack")
                            if (asdu == "s7plus-alarm-ack")
                            {
                                Log("MongoDB CMD CS - " + srv.name + " - Alarm ACK for cpuAlarmId " + address);
                                string ackResult;
                                bool ackOk = false;
=======
                            // Alarm acknowledgement command — bypass tag write path
                            if (asdu == "s7plus-alarm-ack")
                            {
                                Log("MongoDB CMD CS - " + srv.name + " - Alarm ack for cpuAlarmId: " + address);
                                bool ackSuccess = false;
                                string ackResultDescription = "";
>>>>>>> ae59194e49a7e6d197412241d1144eea4e47d43c
                                try
                                {
                                    ulong cpuAlarmId = ulong.Parse(address);

                                    // Queue the ack to AlarmThread, which sends it via alarmConn —
                                    // the dedicated alarm subscription connection has no polling loop
                                    // contention, and the AckJob completion notification arrives there naturally.
                                    var pending = new PendingAlarmAck { CpuAlarmId = cpuAlarmId };
                                    srv.PendingAcks.Enqueue(pending);
<<<<<<< HEAD
                                    ackOk = await pending.Completion.Task;
                                    ackResult = ackOk ? "OK" : "SendAlarmAck failed";
                                }
                                catch (Exception ex)
                                {
                                    ackResult = "Alarm ACK failed: " + ex.Message;
=======
                                    ackSuccess = await pending.Completion.Task;
                                    ackResultDescription = ackSuccess ? "OK" : "SendAlarmAck failed";
                                }
                                catch (Exception ex)
                                {
                                    ackResultDescription = "Alarm ack exception: " + ex.Message;
                                    Log("MongoDB CMD CS - " + srv.name + " - " + ackResultDescription);
>>>>>>> ae59194e49a7e6d197412241d1144eea4e47d43c
                                }

                                // If ack reached the PLC, update ackState in s7plusAlarmEvents.
                                // The alarm subscription connection receives an unparseable ack
                                // confirmation PDU (driver limitation), so we update MongoDB directly.
<<<<<<< HEAD
                                if (ackOk)
=======
                                if (ackSuccess)
>>>>>>> ae59194e49a7e6d197412241d1144eea4e47d43c
                                {
                                    var alarmCollection = DB.GetCollection<BsonDocument>(AlarmEventsCollectionName);
                                    var alarmFilter = Builders<BsonDocument>.Filter.Eq("cpuAlarmId", address);
                                    var alarmUpdate = Builders<BsonDocument>.Update
                                        .Set("ackState", true)
                                        .Set("ackTimestamp", new BsonDateTime(DateTime.UtcNow));
                                    await alarmCollection.UpdateManyAsync(alarmFilter, alarmUpdate);
                                    Log("MongoDB CMD CS - " + srv.name + " - ackState updated in s7plusAlarmEvents for cpuAlarmId: " + address);
                                }
<<<<<<< HEAD
                                Log("MongoDB CMD CS - " + srv.name + " - Alarm ACK result: " + ackResult);
=======
>>>>>>> ae59194e49a7e6d197412241d1144eea4e47d43c

                                // Update commandsQueue as delivered (same pattern as tag write)
                                filter = new BsonDocument(new BsonDocument("_id", change.FullDocument.id));
                                update = new BsonDocument{ {"$set",
                                    new BsonDocument{
                                        { "delivered", true },
<<<<<<< HEAD
                                        { "ack", ackOk },
                                        { "ackTimeTag", new BsonDateTime(DateTime.Now) },
                                        { "resultDescription", ackResult }
=======
                                        { "ack", ackSuccess },
                                        { "ackTimeTag", new BsonDateTime(DateTime.Now) },
                                        { "resultDescription", ackResultDescription }
>>>>>>> ae59194e49a7e6d197412241d1144eea4e47d43c
                                    }
                                } };
                                await collection.UpdateOneAsync(filter, update);
                                break;
                            }

                            // Resolve the ItemAddress for this tag
                            if (!srv.AddressCache.TryGetValue(address, out ItemAddress itemAddr))
                            {
                                Log("MongoDB CMD CS - " + srv.name + " - Address not found in cache: " + address);
                                filter = new BsonDocument(new BsonDocument("_id", change.FullDocument.id));
                                update = new BsonDocument("$set", new BsonDocument("cancelReason", "address not found"));
                                await collection.UpdateOneAsync(filter, update);
                                break;
                            }

                            PValue writeValue;
                            try
                            {
                                writeValue = ConvertCommandToPValue(asdu, cmdValue, cmdValueString);
                            }
                            catch (Exception ex)
                            {
                                Log("MongoDB CMD CS - " + srv.name + " - Type conversion error! " + ex.Message);
                                filter = new BsonDocument(new BsonDocument("_id", change.FullDocument.id));
                                update = new BsonDocument("$set", new BsonDocument("cancelReason", "type conversion error"));
                                await collection.UpdateOneAsync(filter, update);
                                break;
                            }

                            // Write to PLC
                            var addressList = new List<ItemAddress> { itemAddr };
                            var valueList = new List<PValue> { writeValue };

                            Log("MongoDB CMD CS - " + srv.name + " - Writing to " + address + "...");

                            int res = srv.connection.WriteValues(addressList, valueList, out List<ulong> errors);

                            bool okres = (res == 0);
                            string resultDescription = "";
                            if (!okres)
                            {
                                resultDescription = "WriteValues error code: " + res;
                            }
                            else if (errors != null && errors.Count > 0 && errors[0] != 0)
                            {
                                okres = false;
                                resultDescription = "Item error: 0x" + errors[0].ToString("X");
                            }
                            else
                            {
                                resultDescription = "OK";
                            }

                            Log("MongoDB CMD CS - " + srv.name + " - Address: " +
                                address +
                                " value: " + cmdValue + " valueString: " + cmdValueString +
                                " - Command delivered - " + resultDescription);

                            // update as delivered
                            filter = new BsonDocument(new BsonDocument("_id", change.FullDocument.id));
                            update = new BsonDocument{ {"$set",
                                                        new BsonDocument{
                                                            { "delivered", true },
                                                            { "ack", okres },
                                                            { "ackTimeTag", new BsonDateTime(DateTime.Now) },
                                                            { "resultDescription", resultDescription }
                                                        }
                                                    } };
                            await collection.UpdateOneAsync(filter, update);
                            break;
                        }
                    });
                }
            }
            catch (Exception e)
            {
                Log("Exception MongoCmd");
                Log(e);
                Log(e
                    .ToString()
                    .Substring(0,
                    e.ToString().IndexOf(Environment.NewLine)));
                Thread.Sleep(3000);
            }
        }
        while (true);
    }

    /// <summary>
    /// Converts a command value from json-scada to the appropriate S7CommPlus PValue subclass.
    /// </summary>
    static PValue ConvertCommandToPValue(string asdu, double value, string valueString)
    {
        switch (asdu.ToLower())
        {
            case "bool":
            case "bbool":
                return new ValueBool(value != 0.0);
            case "byte":
                return new ValueByte(Convert.ToByte(value));
            case "char":
            case "wchar":
                return new ValueByte(Convert.ToByte(value));
            case "usint":
                return new ValueUSInt(Convert.ToByte(value));
            case "sint":
                return new ValueSInt(Convert.ToSByte(value));
            case "uint":
            case "word":
                return new ValueUInt(Convert.ToUInt16(value));
            case "int":
                return new ValueInt(Convert.ToInt16(value));
            case "udint":
            case "dword":
                return new ValueUDInt(Convert.ToUInt32(value));
            case "dint":
                return new ValueDInt(Convert.ToInt32(value));
            case "ulint":
            case "lword":
                return new ValueULInt(Convert.ToUInt64(value));
            case "lint":
                return new ValueLInt(Convert.ToInt64(value));
            case "real":
                return new ValueReal(Convert.ToSingle(value));
            case "lreal":
                return new ValueLReal(Convert.ToDouble(value));
            case "string":
            case "wstring":
                return new ValueWString(valueString ?? "");
            default:
                throw new ArgumentException($"Unsupported ASDU type for command: {asdu}");
        }
    }
}
