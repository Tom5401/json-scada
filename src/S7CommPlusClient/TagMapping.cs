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
using S7CommPlusDriver;

partial class MainClass
{
    /// <summary>
    /// Browses the PLC variable tree and enqueues new tags for auto-creation in MongoDB.
    /// </summary>
    static void BrowseAndCreateTags(S7CP_connection srv)
    {
        try
        {
            int res = srv.connection.Browse(out List<VarInfo> varInfoList);
            if (res != 0)
            {
                Log(srv.name + " - Browse failed with error: " + res);
                return;
            }

            Log(srv.name + " - Browse returned " + varInfoList.Count + " variables.");

            int enqueuedForCreation = 0;

            foreach (var varInfo in varInfoList)
            {
                string accessSequence = varInfo.AccessSequence;
                string asdu = SoftdatatypeToAsdu(varInfo.Softdatatype);
                string name = varInfo.Name;

                // Cache the ItemAddress for later read/write operations
                var itemAddr = new ItemAddress(accessSequence);
                srv.AddressCache[accessSequence] = itemAddr;
                srv.SoftdatatypeCache[accessSequence] = varInfo.Softdatatype;

                // Only enqueue creation for addresses not already present in MongoDB.
                if (!srv.InsertedAddresses.Contains(accessSequence))
                {
                    var sv = new S7CPValue
                    {
                        selfPublish = true,
                        createCommandForSupervised = srv.commandsEnabled,
                        address = accessSequence,
                        asdu = asdu,
                        isArray = false,
                        value = 0,
                        valueString = "",
                        valueJson = "",
                        cot = 20, // 20 = interrogation / auto-created
                        serverTimestamp = DateTime.Now,
                        quality = false,
                        conn_number = srv.protocolConnectionNumber,
                        conn_name = srv.name,
                        display_name = name,
                        path = ExtractPathFromName(name),
                    };

                    DataQueue.Enqueue(sv);
                    enqueuedForCreation++;
                }
            }

            Log(
                srv.name
                + " - Enqueued "
                + enqueuedForCreation
                + " new tags for creation ("
                + (varInfoList.Count - enqueuedForCreation)
                + " already existed)."
            );
        }
        catch (Exception e)
        {
            Log(srv.name + " - Exception during Browse: " + e.Message);
            Log(e, LogLevelDetailed);
        }
    }

    /// <summary>
    /// Converts a S7CommPlus Softdatatype constant to an ASDU type string for json-scada.
    /// </summary>
    static string SoftdatatypeToAsdu(uint softdatatype)
    {
        switch (softdatatype)
        {
            case Softdatatype.S7COMMP_SOFTDATATYPE_BOOL: return "Bool";
            case Softdatatype.S7COMMP_SOFTDATATYPE_BBOOL: return "BBool";
            case Softdatatype.S7COMMP_SOFTDATATYPE_BYTE: return "Byte";
            case Softdatatype.S7COMMP_SOFTDATATYPE_CHAR: return "Char";
            case Softdatatype.S7COMMP_SOFTDATATYPE_WORD: return "Word";
            case Softdatatype.S7COMMP_SOFTDATATYPE_INT: return "Int";
            case Softdatatype.S7COMMP_SOFTDATATYPE_DWORD: return "DWord";
            case Softdatatype.S7COMMP_SOFTDATATYPE_DINT: return "DInt";
            case Softdatatype.S7COMMP_SOFTDATATYPE_REAL: return "Real";
            case Softdatatype.S7COMMP_SOFTDATATYPE_LREAL: return "LReal";
            case Softdatatype.S7COMMP_SOFTDATATYPE_LINT: return "LInt";
            case Softdatatype.S7COMMP_SOFTDATATYPE_LWORD: return "LWord";
            case Softdatatype.S7COMMP_SOFTDATATYPE_USINT: return "USInt";
            case Softdatatype.S7COMMP_SOFTDATATYPE_UINT: return "UInt";
            case Softdatatype.S7COMMP_SOFTDATATYPE_UDINT: return "UDInt";
            case Softdatatype.S7COMMP_SOFTDATATYPE_ULINT: return "ULInt";
            case Softdatatype.S7COMMP_SOFTDATATYPE_SINT: return "SInt";
            case Softdatatype.S7COMMP_SOFTDATATYPE_STRING: return "String";
            case Softdatatype.S7COMMP_SOFTDATATYPE_WSTRING: return "WString";
            case Softdatatype.S7COMMP_SOFTDATATYPE_WCHAR: return "WChar";
            case Softdatatype.S7COMMP_SOFTDATATYPE_DATE: return "Date";
            case Softdatatype.S7COMMP_SOFTDATATYPE_TIMEOFDAY: return "TimeOfDay";
            case Softdatatype.S7COMMP_SOFTDATATYPE_TIME: return "Time";
            case Softdatatype.S7COMMP_SOFTDATATYPE_LTIME: return "LTime";
            case Softdatatype.S7COMMP_SOFTDATATYPE_DATEANDTIME: return "DateAndTime";
            case Softdatatype.S7COMMP_SOFTDATATYPE_DTL: return "DTL";
            case Softdatatype.S7COMMP_SOFTDATATYPE_LDT: return "LDT";
            case Softdatatype.S7COMMP_SOFTDATATYPE_LTOD: return "LTOD";
            case Softdatatype.S7COMMP_SOFTDATATYPE_STRUCT: return "Struct";
            case Softdatatype.S7COMMP_SOFTDATATYPE_VARIANT: return "Variant";
            default: return "Unknown_" + softdatatype;
        }
    }

    /// <summary>
    /// Extracts a path component from a VarInfo name (e.g., "DB1.Member" -> "DB1").
    /// </summary>
    static string ExtractPathFromName(string name)
    {
        int lastDot = name.LastIndexOf('.');
        if (lastDot > 0)
            return name.Substring(0, lastDot);
        return "";
    }

    /// <summary>
    /// Performs a read cycle for all known tags on the connection.
    /// </summary>
    static void PerformReadCycle(S7CP_connection srv)
    {
        if (srv.AddressCache.Count == 0) return;

        var addressList = new List<ItemAddress>();
        var addressKeys = new List<string>();

        if (srv.useActiveTagRequests)
        {
            var activeAddresses = GetActiveAddressesForConnection(srv);
            if (activeAddresses == null)
                return;

            foreach (var address in activeAddresses)
            {
                if (srv.AddressCache.TryGetValue(address, out var itemAddress))
                {
                    addressList.Add(itemAddress);
                    addressKeys.Add(address);
                }
            }

            if (addressList.Count == 0)
                return;
        }
        else
        {
            foreach (var kvp in srv.AddressCache)
            {
                addressList.Add(kvp.Value);
                addressKeys.Add(kvp.Key);
            }
        }

        try
        {
            int res = srv.connection.ReadValues(addressList, out List<object> values, out List<ulong> errors);
            if (res != 0)
            {
                Log(srv.name + " - ReadValues failed with error: " + res);
                srv.isConnected = false;
                srv.connection = null;
                return;
            }

            for (int i = 0; i < values.Count; i++)
            {
                if (values[i] == null && errors[i] != 0)
                {
                    Log(srv.name + " - Read error for " + addressKeys[i] + ": 0x" + errors[i].ToString("X"), LogLevelDetailed);
                    continue;
                }

                var sv = ConvertReadValue(values[i], addressKeys[i], srv);
                if (sv != null)
                {
                    DataQueue.Enqueue(sv);
                }
            }
        }
        catch (Exception e)
        {
            Log(srv.name + " - Exception during read cycle: " + e.Message);
            Log(e, LogLevelDetailed);
            srv.isConnected = false;
            srv.connection = null;
        }
    }

    /// <summary>
    /// Converts a read value from S7CommPlusDriver to an S7CPValue for MongoDB update.
    /// </summary>
    static S7CPValue ConvertReadValue(object rawValue, string address, S7CP_connection srv)
    {
        if (rawValue == null) return null;

        rawValue = UnwrapPValue(rawValue);

        double dblValue = 0;
        string strValue = "";
        string jsonValue = "";
        bool isArray = false;
        string asdu = "Unknown";

        if (srv.SoftdatatypeCache.TryGetValue(address, out uint sdt))
        {
            asdu = SoftdatatypeToAsdu(sdt);
        }

        try
        {
            switch (rawValue)
            {
                case bool b:
                    dblValue = b ? 1.0 : 0.0;
                    strValue = b.ToString();
                    break;
                case byte bv:
                    dblValue = bv;
                    strValue = bv.ToString();
                    break;
                case sbyte sbv:
                    dblValue = sbv;
                    strValue = sbv.ToString();
                    break;
                case short sv:
                    dblValue = sv;
                    strValue = sv.ToString();
                    break;
                case ushort usv:
                    dblValue = usv;
                    strValue = usv.ToString();
                    break;
                case int iv:
                    dblValue = iv;
                    strValue = iv.ToString();
                    break;
                case uint uiv:
                    dblValue = uiv;
                    strValue = uiv.ToString();
                    break;
                case long lv:
                    dblValue = lv;
                    strValue = lv.ToString();
                    break;
                case ulong ulv:
                    dblValue = ulv;
                    strValue = ulv.ToString();
                    break;
                case float fv:
                    dblValue = fv;
                    strValue = fv.ToString("G");
                    break;
                case double dv:
                    dblValue = dv;
                    strValue = dv.ToString("G");
                    break;
                case string stv:
                    dblValue = 0;
                    strValue = stv;
                    break;
                case byte[] barr:
                    dblValue = 0;
                    strValue = Convert.ToBase64String(barr);
                    break;
                case Array arr:
                    isArray = true;
                    dblValue = 0;
                    jsonValue = System.Text.Json.JsonSerializer.Serialize(rawValue);
                    strValue = jsonValue;
                    break;
                default:
                    // Complex type - try JSON serialization
                    dblValue = 0;
                    try
                    {
                        jsonValue = System.Text.Json.JsonSerializer.Serialize(rawValue);
                        strValue = jsonValue;
                    }
                    catch
                    {
                        strValue = rawValue.ToString();
                    }
                    break;
            }
        }
        catch (Exception e)
        {
            Log(srv.name + " - Value conversion error for " + address + ": " + e.Message, LogLevelDetailed);
            strValue = rawValue?.ToString() ?? "";
        }

        return new S7CPValue
        {
            selfPublish = false,
            createCommandForSupervised = false,
            address = address,
            asdu = asdu,
            isArray = isArray,
            value = dblValue,
            valueString = strValue,
            valueJson = jsonValue,
            cot = 3, // 3 = spontaneous / cyclic
            serverTimestamp = DateTime.Now,
            quality = true,
            conn_number = srv.protocolConnectionNumber,
            conn_name = srv.name,
            display_name = address,
            path = ExtractPathFromName(address),
        };
    }

    static object UnwrapPValue(object rawValue)
    {
        return rawValue switch
        {
            ValueBool value => value.GetValue(),
            ValueBoolArray value => value.GetValue(),
            ValueByte value => value.GetValue(),
            ValueByteArray value => value.GetValue(),
            ValueUSInt value => value.GetValue(),
            ValueUSIntArray value => value.GetValue(),
            ValueSInt value => value.GetValue(),
            ValueSIntArray value => value.GetValue(),
            ValueWord value => value.GetValue(),
            ValueWordArray value => value.GetValue(),
            ValueUInt value => value.GetValue(),
            ValueUIntArray value => value.GetValue(),
            ValueInt value => value.GetValue(),
            ValueIntArray value => value.GetValue(),
            ValueDWord value => value.GetValue(),
            ValueDWordArray value => value.GetValue(),
            ValueUDInt value => value.GetValue(),
            ValueUDIntArray value => value.GetValue(),
            ValueDInt value => value.GetValue(),
            ValueDIntArray value => value.GetValue(),
            ValueLWord value => value.GetValue(),
            ValueLWordArray value => value.GetValue(),
            ValueULInt value => value.GetValue(),
            ValueULIntArray value => value.GetValue(),
            ValueLInt value => value.GetValue(),
            ValueLIntArray value => value.GetValue(),
            ValueReal value => value.GetValue(),
            ValueRealArray value => value.GetValue(),
            ValueLReal value => value.GetValue(),
            ValueLRealArray value => value.GetValue(),
            ValueWString value => value.GetValue(),
            ValueWStringArray value => value.GetValue(),
            ValueTimestamp value => ValueTimestamp.ToString(value.GetValue()),
            ValueTimestampArray value => value.GetValue(),
            ValueTimespan value => ValueTimespan.ToString(value.GetValue()),
            ValueTimespanArray value => value.GetValue(),
            ValueBlob value => value.GetValue(),
            ValueBlobArray value => value.GetValue(),
            _ => rawValue,
        };
    }
}
