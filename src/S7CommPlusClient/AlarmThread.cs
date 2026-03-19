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

    // TIA Portal standard alarm class IDs mapped to human-readable names.
    // IDs confirmed via PLCSIM trace (see 02-01-SUMMARY.md).
    // For IDs not in this map, BuildAlarmDocument() returns "Unknown (N)".
    private static readonly Dictionary<ushort, string> AlarmClassNames = new Dictionary<ushort, string>
    {
        { 33, "Acknowledgment required" },
    };

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

        return new BsonDocument
        {
            { "cpuAlarmId",        dai.CpuAlarmId.ToString() },
            { "alarmState",        alarmState },
            { "alarmText",         texts?.AlarmText ?? "" },
            { "infoText",          texts?.Infotext ?? "" },
            { "additionalTexts",   additionalTexts },
            { "associatedValues",  associatedValues },
            { "timestamp",         new BsonDateTime(dai.AsCgs.Timestamp) },
            // AckTimestamp is DateTime.UnixEpoch when unacknowledged (protocol sends 0) — confirmed via PLCSIM trace
            { "ackState",          dai.AsCgs.AckTimestamp != DateTime.UnixEpoch },
            { "connectionId",      srv.protocolConnectionNumber },
            { "createdAt",         new BsonDateTime(DateTime.UtcNow) },
            { "priority",          (int)dai.HmiInfo.Priority },
            { "alarmClass",        (int)dai.HmiInfo.AlarmClass },
            { "alarmClassName",    AlarmClassNames.TryGetValue(dai.HmiInfo.AlarmClass, out var cn) ? cn : $"Unknown ({dai.HmiInfo.AlarmClass})" },
            { "groupId",           (int)dai.HmiInfo.GroupId },
            { "allStatesInfo",     (int)dai.AllStatesInfo }
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
