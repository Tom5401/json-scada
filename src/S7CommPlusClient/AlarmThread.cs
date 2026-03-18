/*
 * S7CommPlus Client Protocol driver for {json:scada}
 * {json:scada} - Copyright (c) 2020-2026 - Ricardo L. Olsen
 * S7CommPlusDriver - Copyright (C) 2023 Thomas Wiens, th.wiens@gmx.de
 *
 * Alarm subscription thread — receives PLC alarm notifications
 * and writes them to the s7plusAlarmEvents MongoDB collection.
 */

using System;
using System.Threading;
using MongoDB.Bson;
using MongoDB.Driver;
using S7CommPlusDriver;
using S7CommPlusDriver.Alarming;

partial class MainClass
{
    static void AlarmThread(S7CP_connection srv)
    {
        var alarmConn = new S7CommPlusConnection();
        try
        {
            Log(srv.name + " - AlarmThread: connecting alarm connection...");
            int res = alarmConn.Connect(
                srv.endpointURLs[0],
                srv.password,
                srv.username,
                (int)srv.timeoutMs);
            if (res != 0)
            {
                Log(srv.name + " - AlarmThread: alarm connection failed, error: " + res);
                return;
            }
            Log(srv.name + " - AlarmThread: alarm connection established.");

            Log(srv.name + " - AlarmThread: creating alarm subscription...");
            res = alarmConn.AlarmSubscriptionCreate();
            Log(srv.name + " - AlarmThread: AlarmSubscriptionCreate returned " + res);
            if (res != 0)
            {
                Log(srv.name + " - AlarmThread: subscription create failed, exiting.");
                return;
            }

            var alarmCollection = MongoDatabase
                .GetCollection<BsonDocument>(AlarmEventsCollectionName)
                .WithWriteConcern(WriteConcern.W1);

            Log(srv.name + " - AlarmThread: entering receive loop.");

            // Receive loop
            while (!srv.alarmThreadStop)
            {
                Notification noti;
                try
                {
                    noti = alarmConn.WaitForAlarmNotification(5000);
                }
                catch (NotImplementedException ex)
                {
                    // BUG-03: unknown PDU type in Notification.DeserializeFromPdu — catch, log, continue
                    Log(srv.name + " - AlarmThread: unknown PDU type, skipping. " + ex.Message, LogLevelDetailed);
                    continue;
                }

                if (noti == null)
                {
                    // Timeout or error
                    if (alarmConn.m_LastError != 0)
                    {
                        Log(srv.name + " - AlarmThread: receive error " + alarmConn.m_LastError + ", exiting loop.");
                        break;
                    }
                    // Timeout with no error — normal, just loop again
                    continue;
                }

                if (noti.P2Objects == null || noti.P2Objects.Count == 0)
                {
                    continue;
                }

                AlarmsDai dai;
                try
                {
                    dai = AlarmsDai.FromNotificationObject(noti.P2Objects[0], 1033); // LCID 1033 = English
                }
                catch (Exception ex)
                {
                    Log(srv.name + " - AlarmThread: error parsing alarm notification: " + ex.Message, LogLevelDetailed);
                    continue;
                }

                if (dai == null)
                {
                    // BUG-04: ack-only notification — log + skip, no MongoDB write
                    Log(srv.name + " - AlarmThread: ack-only notification, skipping.", LogLevelDebug);
                    continue;
                }

                // Build and insert alarm event document
                try
                {
                    var doc = BuildAlarmDocument(dai, srv);
                    alarmCollection.InsertOneAsync(doc).GetAwaiter().GetResult();
                    Log(srv.name + " - AlarmThread: alarm event written - cpuAlarmId=" + dai.CpuAlarmId
                        + " state=" + (dai.AsCgs.SubtypeId == (uint)AlarmsAsCgs.SubtypeIds.Coming ? "Coming" : "Going")
                        + " text=" + (dai.AlarmTexts?.AlarmText ?? ""));
                }
                catch (Exception ex)
                {
                    Log(srv.name + " - AlarmThread: MongoDB write error: " + ex.Message);
                }
            }
        }
        catch (Exception ex)
        {
            Log(srv.name + " - AlarmThread: unhandled exception: " + ex.ToString(), LogLevelDetailed);
        }
        finally
        {
            Log(srv.name + " - AlarmThread: deleting alarm subscription...");
            try
            {
                alarmConn.AlarmSubscriptionDelete();
            }
            catch (Exception ex)
            {
                Log(srv.name + " - AlarmThread: error during AlarmSubscriptionDelete: " + ex.Message, LogLevelDetailed);
            }
            try
            {
                alarmConn.Disconnect();
            }
            catch (Exception ex)
            {
                Log(srv.name + " - AlarmThread: error during Disconnect: " + ex.Message, LogLevelDetailed);
            }
            Log(srv.name + " - AlarmThread: alarm connection closed.");
        }
    }

    static BsonDocument BuildAlarmDocument(AlarmsDai dai, S7CP_connection srv)
    {
        string alarmState = dai.AsCgs.SubtypeId == (uint)AlarmsAsCgs.SubtypeIds.Coming
            ? "Coming" : "Going";

        var texts = dai.AlarmTexts;
        var additionalTexts = new BsonArray
        {
            texts?.AdditionalText1 ?? "",
            texts?.AdditionalText2 ?? "",
            texts?.AdditionalText3 ?? "",
            texts?.AdditionalText4 ?? "",
            texts?.AdditionalText5 ?? "",
            texts?.AdditionalText6 ?? "",
            texts?.AdditionalText7 ?? "",
            texts?.AdditionalText8 ?? "",
            texts?.AdditionalText9 ?? "",
        };

        return new BsonDocument
        {
            { "cpuAlarmId",       (long)dai.CpuAlarmId },
            { "alarmState",       alarmState },
            { "alarmText",        texts?.AlarmText ?? "" },
            { "infoText",         texts?.Infotext ?? "" },
            { "additionalTexts",  additionalTexts },
            { "timestamp",        new BsonDateTime(dai.AsCgs.Timestamp) },
            { "ackState",         dai.AsCgs.AckTimestamp != DateTime.MinValue },
            { "connectionId",     srv.protocolConnectionNumber },
            { "createdAt",        new BsonDateTime(DateTime.UtcNow) },
            { "priority",         (int)dai.HmiInfo.Priority },
            { "alarmClass",       (int)dai.HmiInfo.AlarmClass },
            { "groupId",          (int)dai.HmiInfo.GroupId },
            { "allStatesInfo",    (int)dai.AllStatesInfo }
        };
    }
}
