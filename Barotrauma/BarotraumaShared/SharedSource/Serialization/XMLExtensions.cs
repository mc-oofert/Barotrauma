﻿using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Xna.Framework;
using File = Barotrauma.IO.File;
using FileStream = Barotrauma.IO.FileStream;
using Path = Barotrauma.IO.Path;

namespace Barotrauma
{
    public static class XMLExtensions
    {
        private static ImmutableDictionary<Type, Func<string, object, object>> converters
            = new Dictionary<Type, Func<string, object, object>>()
            {
                { typeof(string), (str, defVal) => str },
                { typeof(int), (str, defVal) => int.TryParse(str, NumberStyles.Any, CultureInfo.InvariantCulture, out int result) ? result : defVal },
                { typeof(uint), (str, defVal) => uint.TryParse(str, NumberStyles.Any, CultureInfo.InvariantCulture, out uint result) ? result : defVal },
                { typeof(UInt64), (str, defVal) => UInt64.TryParse(str, NumberStyles.Any, CultureInfo.InvariantCulture, out UInt64 result) ? result : defVal },
                { typeof(float), (str, defVal) => float.TryParse(str, NumberStyles.Any, CultureInfo.InvariantCulture, out float result) ? result : defVal },
                { typeof(bool), (str, defVal) => bool.TryParse(str, out bool result) ? result : defVal },
                { typeof(Color), (str, defVal) => ParseColor(str) },
                { typeof(Vector2), (str, defVal) => ParseVector2(str) },
                { typeof(Vector3), (str, defVal) => ParseVector3(str) },
                { typeof(Vector4), (str, defVal) => ParseVector4(str) },
                { typeof(Rectangle), (str, defVal) => ParseRect(str, true) }
            }.ToImmutableDictionary();
        
        public static string ParseContentPathFromUri(this XObject element)
            => !string.IsNullOrWhiteSpace(element.BaseUri)
                ? System.IO.Path.GetRelativePath(Environment.CurrentDirectory, element.BaseUri.CleanUpPath())
                : "";

        public static readonly XmlReaderSettings ReaderSettings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            IgnoreWhitespace = true,
        };

        public static XmlReader CreateReader(System.IO.Stream stream, string baseUri = "")
            => XmlReader.Create(stream, ReaderSettings, baseUri);
        
        public static XDocument TryLoadXml(System.IO.Stream stream)
        {
            XDocument doc;
            try
            {
                using XmlReader reader = CreateReader(stream);
                doc = XDocument.Load(reader);
            }
            catch (Exception e)
            {
                DebugConsole.ThrowError($"Couldn't load xml document from stream!", e);
                return null;
            }
            if (doc?.Root == null)
            {
                DebugConsole.ThrowError("XML could not be loaded from stream: Document or the root element is invalid!");
                return null;
            }
            return doc;
        }
        
        public static XDocument TryLoadXml(string filePath)
        {
            XDocument doc;
            try
            {
                ToolBox.IsProperFilenameCase(filePath);
                using FileStream stream = File.Open(filePath, System.IO.FileMode.Open, System.IO.FileAccess.Read);
                using XmlReader reader = CreateReader(stream, Path.GetFullPath(filePath));
                doc = XDocument.Load(reader, LoadOptions.SetBaseUri);
            }
            catch (Exception e)
            {
                DebugConsole.ThrowError("Couldn't load xml document \"" + filePath + "\"!", e);
                return null;
            }
            if (doc?.Root == null)
            {
                DebugConsole.ThrowError("File \"" + filePath + "\" could not be loaded: Document or the root element is invalid!");
                return null;
            }
            return doc;
        }

        public static XDocument LoadXml(string filePath)
        {
            XDocument doc = null;

            ToolBox.IsProperFilenameCase(filePath);

            if (File.Exists(filePath))
            {
                try
                {
                    using FileStream stream = File.Open(filePath, System.IO.FileMode.Open, System.IO.FileAccess.Read);
                    using XmlReader reader = CreateReader(stream, Path.GetFullPath(filePath));
                    doc = XDocument.Load(reader);
                }
                catch
                {
                    return null;
                }

                if (doc.Root == null) { return null; }
            }

            return doc;
        }

        public static object GetAttributeObject(XAttribute attribute)
        {
            if (attribute == null) { return null; }

            return ParseToObject(attribute.Value.ToString());
        }

        public static object ParseToObject(string value)
        {
            if (value.Contains(".") && Single.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float floatVal))
            {
                return floatVal;
            }
            if (Int32.TryParse(value, out int intVal))
            {
                return intVal;
            }
            
            string lowerTrimmedVal = value.ToLowerInvariant().Trim();
            if (lowerTrimmedVal == "true")
            {
                return true;
            }
            if (lowerTrimmedVal == "false")
            {
                return false;
            }

            return value;
        }


        public static string GetAttributeString(this XElement element, string name, string defaultValue)
        {
            if (element?.Attribute(name) == null) { return defaultValue; }
            return GetAttributeString(element.Attribute(name), defaultValue);
        }

        private static string GetAttributeString(XAttribute attribute, string defaultValue)
        {
            string value = attribute.Value;
            return String.IsNullOrEmpty(value) ? defaultValue : value;
        }

        public static string[] GetAttributeStringArray(this XElement element, string name, string[] defaultValue, bool trim = true, bool convertToLowerInvariant = false)
        {
            if (element?.Attribute(name) == null) { return defaultValue; }

            string stringValue = element.Attribute(name).Value;
            if (string.IsNullOrEmpty(stringValue)) { return defaultValue; }

            string[] splitValue = stringValue.Split(',', '，');

            if (convertToLowerInvariant)
            {
                for (int i = 0; i < splitValue.Length; i++)
                {
                    splitValue[i] = splitValue[i].ToLowerInvariant();
                }
            }
            if (trim)
            {
                for (int i = 0; i < splitValue.Length; i++)
                {
                    splitValue[i] = splitValue[i].Trim();
                }
            }

            return splitValue;
        }

        public static float GetAttributeFloat(this XElement element, float defaultValue, params string[] matchingAttributeName)
        {
            if (element == null) { return defaultValue; }

            foreach (string name in matchingAttributeName)
            {
                if (element.Attribute(name) == null) { continue; }

                float val;
                try
                {
                    string strVal = element.Attribute(name).Value;
                    if (strVal.LastOrDefault() == 'f')
                    {
                        strVal = strVal.Substring(0, strVal.Length - 1);
                    }
                    val = float.Parse(strVal, CultureInfo.InvariantCulture);
                }
                catch (Exception e)
                {
                    DebugConsole.ThrowError("Error in " + element + "!", e);
                    continue;
                }
                return val;
            }

            return defaultValue;
        }

        public static float GetAttributeFloat(this XElement element, string name, float defaultValue)
        {
            if (element?.Attribute(name) == null) { return defaultValue; }

            float val = defaultValue;
            try
            {
                string strVal = element.Attribute(name).Value;
                if (strVal.LastOrDefault() == 'f')
                {
                    strVal = strVal.Substring(0, strVal.Length - 1);
                }
                val = float.Parse(strVal, CultureInfo.InvariantCulture);
            }
            catch (Exception e)
            {
                DebugConsole.ThrowError("Error in " + element + "!", e);
            }

            return val;
        }

        public static float GetAttributeFloat(this XAttribute attribute, float defaultValue)
        {
            if (attribute == null) { return defaultValue; }

            float val = defaultValue;

            try
            {
                string strVal = attribute.Value;
                if (strVal.LastOrDefault() == 'f')
                {
                    strVal = strVal.Substring(0, strVal.Length - 1);
                }
                val = float.Parse(strVal, CultureInfo.InvariantCulture);
            }
            catch (Exception e)
            {
                DebugConsole.ThrowError("Error in " + attribute + "! ", e);
            }

            return val;
        }

        public static float[] GetAttributeFloatArray(this XElement element, string name, float[] defaultValue)
        {
            if (element?.Attribute(name) == null) { return defaultValue; }

            string stringValue = element.Attribute(name).Value;
            if (string.IsNullOrEmpty(stringValue)) { return defaultValue; }

            string[] splitValue = stringValue.Split(',');
            float[] floatValue = new float[splitValue.Length];
            for (int i = 0; i < splitValue.Length; i++)
            {
                try
                {
                    string strVal = splitValue[i];
                    if (strVal.LastOrDefault() == 'f')
                    {
                        strVal = strVal.Substring(0, strVal.Length - 1);
                    }
                    floatValue[i] = float.Parse(strVal, CultureInfo.InvariantCulture);
                }
                catch (Exception e)
                {
                    DebugConsole.ThrowError("Error in " + element + "! ", e);
                }
            }

            return floatValue;
        }

        public static int GetAttributeInt(this XElement element, string name, int defaultValue)
        {
            if (element?.Attribute(name) == null) { return defaultValue; }

            int val = defaultValue;

            try
            {
                if (!Int32.TryParse(element.Attribute(name).Value, NumberStyles.Any, CultureInfo.InvariantCulture, out val))
                {
                    val = (int)float.Parse(element.Attribute(name).Value, CultureInfo.InvariantCulture);
                }
            }
            catch (Exception e)
            {
                DebugConsole.ThrowError("Error in " + element + "! ", e);
            }

            return val;
        }

        public static uint GetAttributeUInt(this XElement element, string name, uint defaultValue)
        {
            if (element?.Attribute(name) == null) { return defaultValue; }

            uint val = defaultValue;

            try
            {
                val = UInt32.Parse(element.Attribute(name).Value);
            }
            catch (Exception e)
            {
                DebugConsole.ThrowError("Error in " + element + "! ", e);
            }

            return val;
        }

        public static UInt64 GetAttributeUInt64(this XElement element, string name, UInt64 defaultValue)
        {
            if (element?.Attribute(name) == null) { return defaultValue; }

            UInt64 val = defaultValue;

            try
            {
                val = UInt64.Parse(element.Attribute(name).Value);
            }
            catch (Exception e)
            {
                DebugConsole.ThrowError("Error in " + element + "! ", e);
            }

            return val;
        }

        public static UInt64 GetAttributeSteamID(this XElement element, string name, UInt64 defaultValue)
        {
            if (element?.Attribute(name) == null) { return defaultValue; }

            UInt64 val = defaultValue;

            try
            {
                val = Steam.SteamManager.SteamIDStringToUInt64(element.Attribute(name).Value);
            }
            catch (Exception e)
            {
                DebugConsole.ThrowError("Error in " + element + "! ", e);
            }

            return val;
        }

        public static int[] GetAttributeIntArray(this XElement element, string name, int[] defaultValue)
        {
            if (element?.Attribute(name) == null) { return defaultValue; }

            string stringValue = element.Attribute(name).Value;
            if (string.IsNullOrEmpty(stringValue)) { return defaultValue; }

            string[] splitValue = stringValue.Split(',');
            int[] intValue = new int[splitValue.Length];
            for (int i = 0; i < splitValue.Length; i++)
            {
                try
                {
                    int val = Int32.Parse(splitValue[i]);
                    intValue[i] = val;
                }
                catch (Exception e)
                {
                    DebugConsole.ThrowError("Error in " + element + "! ", e);
                }
            }

            return intValue;
        }
        public static ushort[] GetAttributeUshortArray(this XElement element, string name, ushort[] defaultValue)
        {
            if (element?.Attribute(name) == null) { return defaultValue; }

            string stringValue = element.Attribute(name).Value;
            if (string.IsNullOrEmpty(stringValue)) { return defaultValue; }

            string[] splitValue = stringValue.Split(',');
            ushort[] ushortValue = new ushort[splitValue.Length];
            for (int i = 0; i < splitValue.Length; i++)
            {
                try
                {
                    ushort val = ushort.Parse(splitValue[i]);
                    ushortValue[i] = val;
                }
                catch (Exception e)
                {
                    DebugConsole.ThrowError("Error in " + element + "! ", e);
                }
            }

            return ushortValue;
        }

        public static bool GetAttributeBool(this XElement element, string name, bool defaultValue)
        {
            if (element?.Attribute(name) == null) { return defaultValue; }
            return element.Attribute(name).GetAttributeBool(defaultValue);
        }

        public static bool GetAttributeBool(this XAttribute attribute, bool defaultValue)
        {
            if (attribute == null) { return defaultValue; }

            string val = attribute.Value.ToLowerInvariant().Trim();
            if (val == "true")
            {
                return true;
            }
            if (val == "false")
            {
                return false;
            }

            DebugConsole.ThrowError("Error in " + attribute.Value.ToString() + "! \"" + val + "\" is not a valid boolean value");
            return false;
        }

        public static Point GetAttributePoint(this XElement element, string name, Point defaultValue)
        {
            if (element?.Attribute(name) == null) { return defaultValue; }
            return ParsePoint(element.Attribute(name).Value);
        }

        public static Vector2 GetAttributeVector2(this XElement element, string name, Vector2 defaultValue)
        {
            if (element?.Attribute(name) == null) { return defaultValue; }
            return ParseVector2(element.Attribute(name).Value);
        }

        public static Vector3 GetAttributeVector3(this XElement element, string name, Vector3 defaultValue)
        {
            if (element == null || element.Attribute(name) == null) { return defaultValue; }
            return ParseVector3(element.Attribute(name).Value);
        }

        public static Vector4 GetAttributeVector4(this XElement element, string name, Vector4 defaultValue)
        {
            if (element == null || element.Attribute(name) == null) { return defaultValue; }
            return ParseVector4(element.Attribute(name).Value);
        }

        public static Color GetAttributeColor(this XElement element, string name, Color defaultValue)
        {
            if (element == null || element.Attribute(name) == null) { return defaultValue; }
            return ParseColor(element.Attribute(name).Value);
        }

        public static Color? GetAttributeColor(this XElement element, string name)
        {
            if (element == null || element.Attribute(name) == null) { return null; }
            return ParseColor(element.Attribute(name).Value);
        }

        public static Color[] GetAttributeColorArray(this XElement element, string name, Color[] defaultValue)
        {
            if (element?.Attribute(name) == null) { return defaultValue; }

            string stringValue = element.Attribute(name).Value;
            if (string.IsNullOrEmpty(stringValue)) { return defaultValue; }

            string[] splitValue = stringValue.Split(';');
            Color[] colorValue = new Color[splitValue.Length];
            for (int i = 0; i < splitValue.Length; i++)
            {
                try
                {
                    Color val = ParseColor(splitValue[i], true);
                    colorValue[i] = val;
                }
                catch (Exception e)
                {
                    DebugConsole.ThrowError("Error in " + element + "! ", e);
                }
            }

            return colorValue;
        }

        public static Rectangle GetAttributeRect(this XElement element, string name, Rectangle defaultValue)
        {
            if (element == null || element.Attribute(name) == null) { return defaultValue; }
            return ParseRect(element.Attribute(name).Value, false);
        }

        //TODO: nested tuples and and n-uples where n!=2 are unsupported
        public static (T1, T2) GetAttributeTuple<T1, T2>(this XElement element, string name, (T1, T2) defaultValue)
        {
            string strValue = element.GetAttributeString(name, $"({defaultValue.Item1}, {defaultValue.Item2})").Trim();

            return ParseTuple(strValue, defaultValue);
        }

        public static (T1, T2)[] GetAttributeTupleArray<T1, T2>(this XElement element, string name,
            (T1, T2)[] defaultValue)
        {
            if (element?.Attribute(name) == null) { return defaultValue; }

            string stringValue = element.Attribute(name).Value;
            if (string.IsNullOrEmpty(stringValue)) { return defaultValue; }

            return stringValue.Split(';').Select(s => ParseTuple<T1, T2>(s, default)).ToArray();
        }

        public static string ElementInnerText(this XElement el)
        {
            StringBuilder str = new StringBuilder();
            foreach (XNode element in el.DescendantNodes().Where(x => x.NodeType == XmlNodeType.Text))
            {
                str.Append(element.ToString());
            }
            return str.ToString();
        }

        public static string PointToString(Point point)
        {
            return point.X.ToString() + "," + point.Y.ToString();
        }

        public static string Vector2ToString(Vector2 vector)
        {
            return vector.X.ToString("G", CultureInfo.InvariantCulture) + "," + vector.Y.ToString("G", CultureInfo.InvariantCulture);
        }

        public static string Vector3ToString(Vector3 vector, string format = "G")
        {
            return vector.X.ToString(format, CultureInfo.InvariantCulture) + "," +
                   vector.Y.ToString(format, CultureInfo.InvariantCulture) + "," +
                   vector.Z.ToString(format, CultureInfo.InvariantCulture);
        }

        public static string Vector4ToString(Vector4 vector, string format = "G")
        {
            return vector.X.ToString(format, CultureInfo.InvariantCulture) + "," +
                   vector.Y.ToString(format, CultureInfo.InvariantCulture) + "," +
                   vector.Z.ToString(format, CultureInfo.InvariantCulture) + "," +
                   vector.W.ToString(format, CultureInfo.InvariantCulture);
        }

        [Obsolete("Prefer XMLExtensions.ToStringHex")]
        public static string ColorToString(Color color)
            => $"{color.R},{color.G},{color.B},{color.A}";

        public static string ToStringHex(this Color color)
            => $"#{color.R:X2}{color.G:X2}{color.B:X2}"
                + ((color.A < 255) ? $"{color.A:X2}" : "");

        public static string RectToString(Rectangle rect)
        {
            return rect.X + "," + rect.Y + "," + rect.Width + "," + rect.Height;
        }

        public static (T1, T2) ParseTuple<T1, T2>(string strValue, (T1, T2) defaultValue)
        {
            strValue = strValue.Trim();
            //require parentheses
            if (strValue[0] != '(' || strValue[^1] != ')') { return defaultValue; }
            //remove parentheses
            strValue = strValue[1..^1];

            string[] elems = strValue.Split(',');
            if (elems.Length != 2) { return defaultValue; }
            
            return ((T1)converters[typeof(T1)].Invoke(elems[0], defaultValue.Item1),
                (T2)converters[typeof(T2)].Invoke(elems[1], defaultValue.Item2));
        }
        
        public static Point ParsePoint(string stringPoint, bool errorMessages = true)
        {
            string[] components = stringPoint.Split(',');
            Point point = Point.Zero;

            if (components.Length != 2)
            {
                if (!errorMessages) { return point; }
                DebugConsole.ThrowError("Failed to parse the string \"" + stringPoint + "\" to Vector2");
                return point;
            }

            int.TryParse(components[0], NumberStyles.Any, CultureInfo.InvariantCulture, out point.X);
            int.TryParse(components[1], NumberStyles.Any, CultureInfo.InvariantCulture, out point.Y);
            return point;
        }

        public static Vector2 ParseVector2(string stringVector2, bool errorMessages = true)
        {
            string[] components = stringVector2.Split(',');

            Vector2 vector = Vector2.Zero;

            if (components.Length != 2)
            {
                if (!errorMessages) { return vector; }
                DebugConsole.ThrowError("Failed to parse the string \"" + stringVector2 + "\" to Vector2");
                return vector;
            }

            float.TryParse(components[0], NumberStyles.Any, CultureInfo.InvariantCulture, out vector.X);
            float.TryParse(components[1], NumberStyles.Any, CultureInfo.InvariantCulture, out vector.Y);

            return vector;
        }

        public static Vector3 ParseVector3(string stringVector3, bool errorMessages = true)
        {
            string[] components = stringVector3.Split(',');

            Vector3 vector = Vector3.Zero;

            if (components.Length != 3)
            {
                if (!errorMessages) { return vector; }
                DebugConsole.ThrowError("Failed to parse the string \"" + stringVector3 + "\" to Vector3");
                return vector;
            }

            Single.TryParse(components[0], NumberStyles.Any, CultureInfo.InvariantCulture, out vector.X);
            Single.TryParse(components[1], NumberStyles.Any, CultureInfo.InvariantCulture, out vector.Y);
            Single.TryParse(components[2], NumberStyles.Any, CultureInfo.InvariantCulture, out vector.Z);

            return vector;
        }

        public static Vector4 ParseVector4(string stringVector4, bool errorMessages = true)
        {
            string[] components = stringVector4.Split(',');

            Vector4 vector = Vector4.Zero;

            if (components.Length < 3)
            {
                if (errorMessages) { DebugConsole.ThrowError("Failed to parse the string \"" + stringVector4 + "\" to Vector4"); }
                return vector;
            }

            Single.TryParse(components[0], NumberStyles.Float, CultureInfo.InvariantCulture, out vector.X);
            Single.TryParse(components[1], NumberStyles.Float, CultureInfo.InvariantCulture, out vector.Y);
            Single.TryParse(components[2], NumberStyles.Float, CultureInfo.InvariantCulture, out vector.Z);
            if (components.Length > 3)
            {
                Single.TryParse(components[3], NumberStyles.Float, CultureInfo.InvariantCulture, out vector.W);
            }

            return vector;
        }

        public static Color ParseColor(string stringColor, bool errorMessages = true)
        {
            if (stringColor.StartsWith("gui.", StringComparison.OrdinalIgnoreCase))
            {
#if CLIENT
                if (GUI.Style != null)
                {
                    string colorName = stringColor.Substring(4);
                    var property = GUI.Style.GetType().GetProperties().FirstOrDefault(
                        p => p.PropertyType == typeof(Color) &&
                             p.Name.Equals(colorName, StringComparison.OrdinalIgnoreCase));
                    if (property != null)
                    {
                        return (Color)property?.GetValue(GUI.Style);
                    }
                }
#endif
                return Color.White;
            }


            string[] strComponents = stringColor.Split(',');

            Color color = Color.White;

            float[] components = new float[4] { 1.0f, 1.0f, 1.0f, 1.0f };

            if (strComponents.Length == 1)
            {
                bool hexFailed = true;
                stringColor = stringColor.Trim();
                if (stringColor.Length > 0 && stringColor[0] == '#')
                {
                    stringColor = stringColor.Substring(1);

                    if (int.TryParse(stringColor, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int colorInt))
                    {
                        if (stringColor.Length == 6)
                        {
                            colorInt = (colorInt << 8) | 0xff;
                        }
                        components[0] = ((float)((colorInt & 0xff000000) >> 24)) / 255.0f;
                        components[1] = ((float)((colorInt & 0x00ff0000) >> 16)) / 255.0f;
                        components[2] = ((float)((colorInt & 0x0000ff00) >> 8)) / 255.0f;
                        components[3] = ((float)(colorInt & 0x000000ff)) / 255.0f;

                        hexFailed = false;
                    }
                }

                if (hexFailed)
                {
                    if (errorMessages) { DebugConsole.ThrowError("Failed to parse the string \"" + stringColor + "\" to Color"); }
                    return Color.White;
                }
            }
            else
            {
                for (int i = 0; i < 4 && i < strComponents.Length; i++)
                {
                    float.TryParse(strComponents[i], NumberStyles.Float, CultureInfo.InvariantCulture, out components[i]);
                }

                if (components.Any(c => c > 1.0f))
                {
                    for (int i = 0; i < 4; i++)
                    {
                        components[i] = components[i] / 255.0f;
                    }
                    //alpha defaults to 1.0 if not given
                    if (strComponents.Length < 4) components[3] = 1.0f;
                }
            }

            return new Color(components[0], components[1], components[2], components[3]);
        }
        
        public static Rectangle ParseRect(string stringRect, bool requireSize, bool errorMessages = true)
        {
            string[] strComponents = stringRect.Split(',');
            if ((strComponents.Length < 3 && requireSize) || strComponents.Length < 2)
            {
                if (errorMessages) { DebugConsole.ThrowError("Failed to parse the string \"" + stringRect + "\" to Rectangle"); }
                return new Rectangle(0, 0, 0, 0);
            }

            int[] components = new int[4] { 0, 0, 0, 0 };
            for (int i = 0; i < 4 && i < strComponents.Length; i++)
            {
                int.TryParse(strComponents[i], out components[i]);
            }

            return new Rectangle(components[0], components[1], components[2], components[3]);
        }

        public static float[] ParseFloatArray(string[] stringArray)
        {
            if (stringArray == null || stringArray.Length == 0) return null;

            float[] floatArray = new float[stringArray.Length];
            for (int i = 0; i < floatArray.Length; i++)
            {
                floatArray[i] = 0.0f;
                Single.TryParse(stringArray[i], NumberStyles.Float, CultureInfo.InvariantCulture, out floatArray[i]);
            }

            return floatArray;
        }

        public static string[] ParseStringArray(string stringArrayValues)
        {
            return string.IsNullOrEmpty(stringArrayValues) ? new string[0] : stringArrayValues.Split(';');
        }

        public static bool IsOverride(this XElement element) => element.Name.ToString().Equals("override", StringComparison.OrdinalIgnoreCase);
        public static bool IsCharacterVariant(this XElement element) => element.Name.ToString().Equals("charactervariant", StringComparison.OrdinalIgnoreCase);

        public static XElement FirstElement(this XElement element) => element.Elements().FirstOrDefault();

        public static XAttribute GetAttribute(this XElement element, string name, StringComparison comparisonMethod = StringComparison.OrdinalIgnoreCase) => element.GetAttribute(a => a.Name.ToString().Equals(name, comparisonMethod));

        public static XAttribute GetAttribute(this XElement element, Func<XAttribute, bool> predicate) => element.Attributes().FirstOrDefault(predicate);

        /// <summary>
        /// Returns the first child element that matches the name using the provided comparison method.
        /// </summary>
        public static XElement GetChildElement(this XContainer container, string name, StringComparison comparisonMethod = StringComparison.OrdinalIgnoreCase) => container.Elements().FirstOrDefault(e => e.Name.ToString().Equals(name, comparisonMethod));

        /// <summary>
        /// Returns all child elements that match the name using the provided comparison method.
        /// </summary>
        public static IEnumerable<XElement> GetChildElements(this XContainer container, string name, StringComparison comparisonMethod = StringComparison.OrdinalIgnoreCase) => container.Elements().Where(e => e.Name.ToString().Equals(name, comparisonMethod));
    }
}
