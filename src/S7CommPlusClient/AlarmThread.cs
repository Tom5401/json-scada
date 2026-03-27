/*
 * S7CommPlus Client Protocol driver for {json:scada}
 * {json:scada} - Copyright (c) 2020-2026 - Ricardo L. Olsen
 * S7CommPlusDriver - Copyright (C) 2023 Thomas Wiens, th.wiens@gmx.de
 *
 * Alarm subscription thread — receives PLC alarm notifications
 * and writes them to the s7plusAlarmEvents MongoDB collection.
 */

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
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

            // After AlarmSubscriptionCreate() succeeds, poll for already-active alarms
            Log(srv.name + " - AlarmThread: polling for already-active alarms...");
            List<AlarmsDai> activeAlarms;
            int pollRes = alarmConn.GetActiveAlarms(out activeAlarms, 1033); // LCID 1033 = English
            if (pollRes == 0 && activeAlarms.Count > 0)
            {
                Log(srv.name + " - AlarmThread: found " + activeAlarms.Count + " active alarm(s).");
                foreach (var dai in activeAlarms)
                {
                    try
                    {
                        var doc = BuildAlarmDocument(dai, srv);
                        UpsertAlarmEvent(doc, srv);
                    }
                    catch (Exception ex)
                    {
                        Log(srv.name + " - AlarmThread: MongoDB write error for active alarm: " + ex.Message);
                    }
                }
            }
            else if (pollRes != 0)
            {
                Log(srv.name + " - AlarmThread: GetActiveAlarms failed with error " + pollRes + ", continuing with subscription only.");
            }

            Log(srv.name + " - AlarmThread: entering receive loop.");

            // Receive loop
            while (!srv.alarmThreadStop)
            {
                // Drain pending ack requests — send each via alarmConn (dedicated connection,
                // no contention with the polling loop that uses srv.connection).
                while (srv.PendingAcks.TryDequeue(out var pending))
                {
                    Log(srv.name + " - AlarmThread: sending alarm ack for cpuAlarmId: " + pending.CpuAlarmId);
                    int ackRes = alarmConn.SendAlarmAck(pending.CpuAlarmId);
                    pending.Completion.SetResult(ackRes == 0);
                }

                Notification noti;
                try
                {
                    noti = alarmConn.WaitForAlarmNotification(5000);
                }
                catch (NotImplementedException ex)
                {
                    // BUG-03: unknown PDU type in Notification.DeserializeFromPdu — catch, log, continue
                    Log(srv.name + " - AlarmThread: unknown PDU type, skipping. " + ex.Message);
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

                // AckJob completion notifications (ClassId=3636) are not DAI alarm objects —
                // skip them rather than letting FromNotificationObject throw on missing attributes.
                const uint AckJobClassRid = 3636;
                if (noti.P2Objects[0].ClassId == AckJobClassRid)
                {
                    Log(srv.name + " - AlarmThread: AckJob completion notification received, skipping.", LogLevelDebug);
                    continue;
                }

                AlarmsDai dai;
                try
                {
                    dai = AlarmsDai.FromNotificationObject(noti.P2Objects[0], 1033); // LCID 1033 = English
                }
                catch (Exception ex)
                {
                    Log(srv.name + " - AlarmThread: non-alarm notification (ack confirmation), skipping. " + ex.Message, LogLevelDebug);
                    continue;
                }

                if (dai == null)
                {
                    // BUG-04: ack-only notification — log + skip, no MongoDB write
                    Log(srv.name + " - AlarmThread: ack-only notification, skipping.", LogLevelDebug);
                    continue;
                }

                // Build and upsert alarm event document
                try
                {
                    var doc = BuildAlarmDocument(dai, srv);
                    UpsertAlarmEvent(doc, srv);
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

    // TIA Portal standard alarm class IDs mapped to human-readable names.
    // IDs uitgelezen van Levvel_Release45
    // For IDs not in this map, BuildAlarmDocument() returns "Unknown (N)".
    private static readonly Dictionary<ushort, string> AlarmClassNames = new Dictionary<ushort, string>
    {
        { 33, "Acknowledgment - high" },
        { 34, "Acknowledgement (A)" },
        { 35, "No Acknowledgement (NA)" },
        { 36, "1_Urgent"},
        { 37, "2_NietUrgent"},
        { 38, "3_BedieningEnProcess"},
        { 39, "4_UrgentOnderhoud"},
        { 40, "5_NietUrgentOnderhoud"},
        { 41, "6_Attentiesignaal"},
        { 42, "7_BeveiligingEnBewaking"},
        { 43, "8_Logging"},
        { 44, "9_Overig"}
    };

    // Alarm classes that require operator acknowledgement (per TIA Portal configuration).
    // Used to set isAcknowledgeable on every alarm document written to MongoDB.
    private static readonly HashSet<ushort> AcknowledgeableClasses = new HashSet<ushort> { 33, 34, 36, 37, 39 };

    // Upsert by cpuAlarmId — if an event with the same cpuAlarmId already exists, update it;
    // otherwise insert a new document. This handles the case where a "Going" event arrives
    // after its corresponding "Coming" event has already been written, allowing the "Going"
    // event to update the existing document rather than creating a new one.
    static void UpsertAlarmEvent(BsonDocument doc, S7CP_connection srv)
    {
        try
        {
            var alarmCollection = MongoDatabase
                .GetCollection<BsonDocument>(AlarmEventsCollectionName)
                .WithWriteConcern(WriteConcern.W1);

            var filter = Builders<BsonDocument>.Filter.Eq("cpuAlarmId", doc["cpuAlarmId"]);
            var options = new ReplaceOptions { IsUpsert = true };
            alarmCollection.ReplaceOneAsync(filter, doc, options).GetAwaiter().GetResult();
            Log(srv.name + " - UpsertAlarmEvent: upserted alarm event with cpuAlarmId=" + doc["cpuAlarmId"], LogLevelDetailed);
        }
        catch (Exception e)
        {
            Log(srv.name + " - UpsertAlarmEvent: MongoDB upsert error: " + e.Message, LogLevelBasic);
        }        
    }

    static BsonDocument BuildAlarmDocument(AlarmsDai dai, S7CP_connection srv)
    {
        string alarmState = dai.AsCgs.SubtypeId == (uint)AlarmsAsCgs.SubtypeIds.Coming
            ? "Coming" : "Going";

        var texts = dai.AlarmTexts;
        var av    = dai.AsCgs.AssociatedValues;

        var additionalTexts = new BsonArray
        {
            ResolveAlarmText(texts?.AdditionalText1 ?? "", av),
            ResolveAlarmText(texts?.AdditionalText2 ?? "", av),
            ResolveAlarmText(texts?.AdditionalText3 ?? "", av),
            ResolveAlarmText(texts?.AdditionalText4 ?? "", av),
            ResolveAlarmText(texts?.AdditionalText5 ?? "", av),
            ResolveAlarmText(texts?.AdditionalText6 ?? "", av),
            ResolveAlarmText(texts?.AdditionalText7 ?? "", av),
            ResolveAlarmText(texts?.AdditionalText8 ?? "", av),
            ResolveAlarmText(texts?.AdditionalText9 ?? "", av),
        };

        // Raw typed SD values — SD_1..SD_10 as index 0..9
        var associatedValues = new BsonArray
        {
            SdValueToBson(av?.SD_1),
            SdValueToBson(av?.SD_2),
            SdValueToBson(av?.SD_3),
            SdValueToBson(av?.SD_4),
            SdValueToBson(av?.SD_5),
            SdValueToBson(av?.SD_6),
            SdValueToBson(av?.SD_7),
            SdValueToBson(av?.SD_8),
            SdValueToBson(av?.SD_9),
            SdValueToBson(av?.SD_10),
        };

        uint relationId = (uint)(dai.CpuAlarmId >> 32);
        uint dbNumber   = relationId & 0xFFFF;

        return new BsonDocument
        {
            { "cpuAlarmId",        dai.CpuAlarmId.ToString() },
            { "alarmState",        alarmState },
            { "alarmText",         ResolveAlarmText(texts?.AlarmText ?? "", av) },
            { "infoText",          ResolveAlarmText(texts?.Infotext ?? "", av) },
            { "additionalTexts",   additionalTexts },
            { "associatedValues",  associatedValues },
            { "timestamp",         new BsonDateTime(dai.AsCgs.Timestamp) },
            // AckTimestamp is DateTime.UnixEpoch when unacknowledged (protocol sends 0) — confirmed via PLCSIM trace
            { "ackState",          dai.AsCgs.AckTimestamp != DateTime.UnixEpoch },
            { "connectionId",      srv.protocolConnectionNumber },
            { "connectionName",    srv.name ?? "" },
            { "createdAt",         new BsonDateTime(DateTime.UtcNow) },
            { "priority",          (int)dai.HmiInfo.Priority },
            { "alarmClass",        (int)dai.HmiInfo.AlarmClass },
            { "alarmClassName",    AlarmClassNames.TryGetValue(dai.HmiInfo.AlarmClass, out var cn) ? cn : $"Unknown ({dai.HmiInfo.AlarmClass})" },
            { "isAcknowledgeable", AcknowledgeableClasses.Contains(dai.HmiInfo.AlarmClass) },
            { "groupId",           (int)dai.HmiInfo.GroupId },
            { "allStatesInfo",     (int)dai.AllStatesInfo },
            { "relationId",        new BsonInt64((long)relationId) },
            { "dbNumber",          (int)dbNumber },
            { "originDbName",      srv.RelationIdNameMap.TryGetValue(relationId, out var dbName) ? dbName : "" }
        };
    }

    // Resolve TIA Portal alarm text placeholders: @N%f@, @N%d@, @N%s@, etc.
    // N is 1-based SD index; format specifier is ignored — AssociatedValue.ToString() is used.
    static readonly Regex AlarmTextPlaceholder = new Regex(@"@(\d+)%[a-zA-Z]@", RegexOptions.Compiled);

    static string ResolveAlarmText(string template, AlarmsAssociatedValues av)
    {
        if (string.IsNullOrEmpty(template) || av == null || !template.Contains('@'))
            return template;

        return AlarmTextPlaceholder.Replace(template, m =>
        {
            int n = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            AssociatedValue sd = n switch
            {
                1  => av.SD_1,  2  => av.SD_2,  3  => av.SD_3,  4  => av.SD_4,
                5  => av.SD_5,  6  => av.SD_6,  7  => av.SD_7,  8  => av.SD_8,
                9  => av.SD_9,  10 => av.SD_10,
                _  => null
            };
            if (sd == null) return m.Value; // leave placeholder if SD is absent
            // Use invariant culture for real types to match TIA Portal's decimal format
            uint ti = sd.TypeInfo;
            if (ti == Ids.TI_REAL || ti == Ids.TI_LREAL)
                return Convert.ToDouble(sd.ToString(), CultureInfo.CurrentCulture)
                              .ToString("G", CultureInfo.InvariantCulture);
            return sd.ToString();
        });
    }

    // Convert AssociatedValue to the most appropriate BsonValue type
    static BsonValue SdValueToBson(AssociatedValue sd)
    {
        if (sd == null) return BsonNull.Value;
        uint ti = sd.TypeInfo;
        if (ti == Ids.TI_BOOL)
            return new BsonBoolean(sd.ToString() == "True");
        if (ti == Ids.TI_REAL || ti == Ids.TI_LREAL)
            return new BsonDouble(Convert.ToDouble(sd.ToString(), CultureInfo.CurrentCulture));
        if (ti == Ids.TI_BYTE  || ti == Ids.TI_WORD  || ti == Ids.TI_DWORD ||
            ti == Ids.TI_INT   || ti == Ids.TI_DINT  || ti == Ids.TI_USINT ||
            ti == Ids.TI_UINT  || ti == Ids.TI_UDINT || ti == Ids.TI_SINT)
            return new BsonInt64(Convert.ToInt64(sd.ToString(), CultureInfo.InvariantCulture));
        return new BsonString(sd.ToString());
    }
}
