﻿using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace MultiplayerARPG.MMO
{
    public class ConfigReader : MonoBehaviour
    {
        public static bool ReadConfigs(Dictionary<string, object> config, string configName, out string result, string defaultValue = null)
        {
            result = defaultValue;

            if (config == null || !config.ContainsKey(configName))
                return false;

            result = (string)config[configName];
            return true;
        }

        public static bool ReadConfigs(Dictionary<string, object> config, string configName, out int result, int defaultValue = -1)
        {
            result = defaultValue;

            if (config == null || !config.ContainsKey(configName))
                return false;

            result = (int)(long)config[configName];
            return true;
        }

        public static bool ReadConfigs(Dictionary<string, object> config, string configName, out bool result, bool defaultValue = false)
        {
            result = defaultValue;

            if (config == null || !config.ContainsKey(configName))
                return false;

            result = (bool)config[configName];
            return true;
        }

        public static bool ReadConfigs(Dictionary<string, object> config, string configName, out List<string> result, List<string> defaultValue = null)
        {
            result = defaultValue;

            if (config == null || !config.ContainsKey(configName))
                return false;

            result = new List<string>();
            var objResults = (List<object>)config[configName];
            foreach (var objResult in objResults)
            {
                result.Add((string)objResult);
            }
            return true;
        }

        public static bool ReadArgs(string[] args, string argName, out string result, string defaultValue = null)
        {
            result = defaultValue;

            if (args == null)
                return false;

            var argsList = new List<string>(args);
            if (!argsList.Contains(argName))
                return false;

            var index = argsList.FindIndex(0, a => a.Equals(argName));
            result = args[index + 1];
            return true;
        }

        public static bool ReadArgs(string[] args, string argName, out int result, int defaultValue = -1)
        {
            result = defaultValue;
            string text = string.Empty;
            if (ReadArgs(args, argName, out text, defaultValue.ToString()) && int.TryParse(text, out result))
                return true;
            return false;
        }

        public static bool ReadArgs(string[] args, string argName, out List<string> result, List<string> defaultValue = null)
        {
            result = defaultValue;
            string text = string.Empty;
            if (ReadArgs(args, argName, out text, ""))
            {
                result = new List<string>(text.Split('|'));
                return true;
            }
            return false;
        }

        public static bool IsArgsProvided(string[] args, string argName)
        {
            if (args == null)
                return false;

            var argsList = new List<string>(args);
            return argsList.Contains(argName);
        }
    }
}
