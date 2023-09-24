using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.IO;
using System.IO.Compression;

using Utils;

namespace GDCustomText
{
    internal class Program
    {

        #region common stuff

        private static readonly string CWD = Application.StartupPath;

        private static void Exit(string msg, int errCode = 0)
        {
            Console.WriteLine(msg);
            System.Threading.Thread.Sleep(5000);
            Environment.Exit(errCode);
        }

        #endregion

        static void Main(string[] args)
        {
            if (args.Length == 0)
                Exit("Drop one or multiple text_xx.zip files and one or multiple *.txt files containg your strings onto the exe");

            var txtFiles = new List<string>();
            var zipFiles = new List<string>();
            foreach (var arg in args)
            {
                var ext = Path.GetExtension(arg);
                switch(ext)
                {
                    case ".txt":
                        if (File.Exists(arg))
                            txtFiles.Add(arg);
                        else
                            Console.WriteLine($"Txt file not found: {arg}");
                        break;
                    case ".zip":
                        if (File.Exists(arg))
                            zipFiles.Add(arg);
                        else
                            Console.WriteLine($"Zip file not found: {arg}");
                        break;
                    default:
                        Console.WriteLine($"Unsupported extension: {arg}");
                        break;
                }
            }

            if (txtFiles.Count == 0) Exit("No txt files");
            if (zipFiles.Count == 0) Exit("No zip files");

            Console.WriteLine("Creating dictionary");
            var userDict = new Dictionary<string, string>();
            var userDictLocals = new Dictionary<string, Dictionary<string, string>>();
            var keyAlisases = new Dictionary<string, string>();
            foreach (var txtFile in txtFiles)
            {
                Console.WriteLine($"Reading {txtFile}");
                foreach(var line in Funcs.ReadFileLinesIter(txtFile))
                {
                    if (line.StartsWith("="))
                    {
                        var lineSplit = line.Split('=');
                        if (lineSplit.Length == 3)
                            keyAlisases[lineSplit[1]] = lineSplit[2];
                        else
                            Console.Write($"Warning: Unsupported line format: {line}");
                    }
                    else
                    {
                        if (Funcs.GetKeyValueFromString(line, out var kvp, '='))
                        {
                            // handling /lang/key=value pair
                            if (kvp.Key.StartsWith("/"))
                            {
                                var keySplit = kvp.Key.Split('/');
                                if (keySplit.Length != 3)
                                {
                                    Console.Write($"Warning: Unsupported line format: {line}");
                                }
                                else
                                {
                                    var lang = keySplit[1].ToLower();
                                    var key = keySplit[2];
                                    if (!userDictLocals.ContainsKey(lang))
                                        userDictLocals[lang] = new Dictionary<string, string>();

                                    if (userDictLocals[lang].ContainsKey(key))
                                    {
                                        Console.Write($"Warning: Overwriting duplicate key: {kvp.Key}");
                                        Console.Write($"  Old: {userDictLocals[lang][key]}");
                                        Console.Write($"  New: {kvp.Value}");
                                    }
                                    userDictLocals[lang][key] = kvp.Value;
                                }
                            }
                            else
                            {
                                if (userDict.ContainsKey(kvp.Key))
                                {
                                    Console.Write($"Warning: Overwriting duplicate key: {kvp.Key}");
                                    Console.Write($"  Old: {userDict[kvp.Key]}");
                                    Console.Write($"  New: {kvp.Value}");
                                }
                                userDict[kvp.Key] = kvp.Value;
                            }
                        }
                    }
                }
            }
            if (userDict.Count == 0)
                Exit("No tag=string pairs found inside txt file(s)");

            foreach(var zipFile in zipFiles)
            {
                Console.WriteLine($"Processing zip file: {zipFile}");
                var lowerZipName = Path.GetFileNameWithoutExtension(zipFile).ToLower();
                var localDict = new Dictionary<string, string>();
                var localDictKey = "";
                foreach(var kvpLocalDicts in userDictLocals)
                {
                    if (lowerZipName.StartsWith(kvpLocalDicts.Key))
                    {
                        if (localDictKey.Length < kvpLocalDicts.Key.Length)
                        {
                            localDictKey = kvpLocalDicts.Key;
                            localDict = kvpLocalDicts.Value;
                        }
                    }
                }

                var foundKeys = new Dictionary<string, bool>();
                foreach (var kvp in userDict)
                    foundKeys[kvp.Key] = false;
                foreach (var kvp in localDict)
                    foundKeys[kvp.Key] = false;

                using (var zip = ZipFile.Open(zipFile, ZipArchiveMode.Update))
                {

                    // newEntries[entryPath] = newLines[]
                    var newEntries = new Dictionary<string, List<string>>();
                    foreach (var entry in zip.Entries)
                    {
                        var newLines = new List<string>();
                        var writeNewLines = false;
                        using (var entryStream = entry.Open())
                        {
                            foreach (var entryLine in Funcs.ReadStreamLinesIter(entryStream))
                            {
                                if (!Funcs.GetKeyValueFromString(entryLine, out var kvp, '='))
                                {
                                    // line is not key=value pair
                                    newLines.Add(entryLine);
                                    continue;
                                }

                                var key = kvp.Key;
                                var value = kvp.Value;

                                // key alias exists
                                if (keyAlisases.ContainsKey(key))
                                {
                                    Console.WriteLine($"  KEY ALIAS: {key} => {keyAlisases[key]}");
                                    key = keyAlisases[key];
                                    writeNewLines = true;
                                }
                                
                                var customLine = $"{key}={value}";
                                if (foundKeys.ContainsKey(key))
                                {
                                    foundKeys[key] = true;

                                    if (localDict.ContainsKey(key))
                                        customLine = $"{key}={localDict[key]}";
                                    else
                                        customLine = $"{key}={userDict[key]}";

                                    Console.WriteLine($"  {customLine}");
                                    newLines.Add(customLine);
                                    writeNewLines = true;
                                }
                                else
                                {
                                    newLines.Add(customLine);
                                }
                            }
                        }

                        if (writeNewLines)
                            newEntries.Add(entry.FullName, newLines);
                    }
                    foreach (var toAdd in newEntries)
                    {
                        var entryPath = toAdd.Key;
                        var entryLines = toAdd.Value;
                        Console.WriteLine($"Writing: {entryPath}");
                        zip.GetEntry(entryPath).Delete();
                        var entry = zip.CreateEntry(entryPath);
                        using (var writer = new StreamWriter(entry.Open()))
                        {
                            foreach (var line in entryLines)
                                writer.WriteLine(line);
                        }
                    }
                    // missing strings ?
                    if (foundKeys.Any((kvp) => kvp.Value == false))
                    {
                        var entryPath = "tags_missing.txt";
                        var missingTxt = zip.GetEntry(entryPath);
                        if (missingTxt == null)
                            missingTxt = zip.CreateEntry(entryPath);
                        Console.WriteLine($"Writing: {entryPath}");

                        using (var stream = missingTxt.Open())
                        {
                            stream.Seek(0, SeekOrigin.End);
                            using (var writer = new StreamWriter(stream))
                            {
                                foreach (var kvp in foundKeys)
                                {
                                    if (kvp.Value == false)
                                    {
                                        var customLine = "";
                                        if (localDict.ContainsKey(kvp.Key))
                                            customLine = $"{kvp.Key}={localDict[kvp.Key]}";
                                        else
                                            customLine = $"{kvp.Key}={userDict[kvp.Key]}";
                                        Console.WriteLine($"  {customLine}");
                                        writer.WriteLine(customLine);
                                    }
                                }
                            }

                        }
                    }
                }

            }

            Exit("Done");
        }
    }
}
