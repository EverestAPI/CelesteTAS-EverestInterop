using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;

namespace StudioCommunication;

public static class BinaryHelper {
    
    public static void SerializeObject(object obj, BinaryWriter writer) {
        switch (obj)
        {
            // Primitives
            case bool v:
                writer.Write(v);
                break;
            case byte v:
                writer.Write(v);
                break;
            case char v:
                writer.Write(v);
                break;
            case decimal v:
                writer.Write(v);
                break;
            case double v:
                writer.Write(v);
                break;
            case float v:
                writer.Write(v);
                break;
            case int v:
                writer.Write(v);
                break;
            case long v:
                writer.Write(v);
                break;
            case sbyte v:
                writer.Write(v);
                break;
            case short v:
                writer.Write(v);
                break;
            case Half v:
                writer.Write(v);
                break;
            case string v:
                writer.Write(v);
                break;
            
            // Collections
            case IList v:
                writer.Write7BitEncodedInt(v.Count);
                foreach (var item in v) {
                    SerializeObject(item, writer);
                }
                break;

            // For Vector2
            case ValueTuple<float, float> v:
                writer.Write(v.Item1);
                writer.Write(v.Item2);
                break;

            default:
                if (obj.GetType().IsEnum) {
                    writer.Write((int) obj);
                    break;
                } else {
                    throw new Exception($"Unsupported type: {obj.GetType()}");
                }
        }
    }
    
    public static object DeserializeObject(Type type, BinaryReader reader)
    {
        // Primitives
        if (type == typeof(bool))
            return reader.ReadBoolean();
        if (type == typeof(byte))
            return reader.ReadByte();
        if (type == typeof(byte[]))
            return reader.ReadBytes(reader.Read7BitEncodedInt());
        if (type == typeof(char))
            return reader.ReadChar();
        if (type == typeof(char[]))
            return reader.ReadChars(reader.Read7BitEncodedInt());
        if (type == typeof(decimal))
            return reader.ReadDecimal();
        if (type == typeof(double))
            return reader.ReadDouble();
        if (type == typeof(float))
            return reader.ReadSingle();
        if (type == typeof(int))
            return reader.ReadInt32();
        if (type == typeof(long))
            return reader.ReadInt64();
        if (type == typeof(sbyte))
            return reader.ReadSByte();
        if (type == typeof(short))
            return reader.ReadInt16();
        if (type == typeof(Half))
            return reader.ReadHalf();
        if (type == typeof(string))
            return reader.ReadString();

        // Collections
        if (type.IsAssignableTo(typeof(IList)) && type.IsGenericType)
        {
            var itemType = type.GenericTypeArguments[0];
            var list = (IList)Activator.CreateInstance(type)!;
            int count = reader.Read7BitEncodedInt();
            for (int i = 0; i < count; i++) {
                list.Add(DeserializeObject(itemType, reader));
            }
            
            return list;
        }

        // For Vector2
        if (type == typeof((float, float))) {
            return (reader.ReadSingle(), reader.ReadSingle());
        }
        
        throw new Exception($"Unsupported type: {type}");
    }

    public static void SerializeDictionary<TKey, TValue>(Dictionary<TKey, TValue> dict, BinaryWriter writer) where TKey : notnull where TValue : notnull {
        writer.Write7BitEncodedInt(dict.Count);
        foreach (var (key, value) in dict) {
            SerializeObject(key, writer);
            SerializeObject(value, writer);
        }
    }
    public static Dictionary<TKey, TValue> DeserializeDictionary<TKey, TValue>(BinaryReader reader) where TKey : notnull where TValue : notnull {
        int capacity = reader.Read7BitEncodedInt();
        var dict = new Dictionary<TKey, TValue>(capacity);

        for (int i = 0; i < capacity; i++) {
            var key = (TKey)DeserializeObject(typeof(TKey), reader);
            var value = (TValue)DeserializeObject(typeof(TValue), reader);
            dict[key] = value;
        }

        return dict;
    }
}