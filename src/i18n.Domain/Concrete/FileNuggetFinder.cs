﻿using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Text;
using i18n.Domain.Abstract;
using i18n.Domain.Entities;
using i18n.Domain.Helpers;
using i18n.Helpers;

namespace i18n.Domain.Concrete
{
    public class FileNuggetFinder : INuggetFinder
    {
        private i18nSettings _settings;

        private NuggetParser _nuggetParser;

        public FileNuggetFinder(i18nSettings settings)
        {
            _settings = settings;
            _nuggetParser = new NuggetParser(new NuggetTokens(
                _settings.NuggetBeginToken,
                _settings.NuggetEndToken,
                _settings.NuggetDelimiterToken,
                _settings.NuggetCommentToken),
                NuggetParser.Context.SourceProcessing);
        }

        /// <summary>
        /// Goes through the Directories to scan recursively and starts a scan of each while that matches the whitelist. (both from settings)
        /// </summary>
        /// <returns>All found nuggets.</returns>
        public IDictionary<string, TemplateItem> ParseAll()
        {
            IEnumerable<string> fileWhiteList = _settings.WhiteList;
            IEnumerable<string> directoriesToSearchRecursively = _settings.DirectoriesToScan;
            FileEnumerator fileEnumerator = new FileEnumerator(_settings.BlackList);

            string currentFullPath;
            bool blacklistFound = false;

            var templateItems = new ConcurrentDictionary<string, TemplateItem>();
                // Collection of template items keyed by their id.

            foreach (var directoryPath in directoriesToSearchRecursively)
            {
                foreach (string filePath in fileEnumerator.GetFiles(directoryPath))
                {
                    if (filePath.Length >= 260)
                    {
                        DebugHelpers.WriteLine("Path too long to process. Path: " + filePath);
                        continue;
                    }

                    blacklistFound = false;
                    currentFullPath = Path.GetDirectoryName(Path.GetFullPath(filePath));
                    foreach (var blackItem in _settings.BlackList)
                    {
                        if (currentFullPath == null || currentFullPath.StartsWith(blackItem, StringComparison.OrdinalIgnoreCase))
                        {
                            //this is a file that is under a blacklisted directory so we do not parse it.
                            blacklistFound = true;
                            break;
                        }
                    }
                    if (!blacklistFound)
                    {


                        //we check every filePath against our white list. if it's on there in at least one form we check it.
                        foreach (var whiteListItem in fileWhiteList)
                        {
                            //We have a catch all for a filetype
                            if (whiteListItem.StartsWith("*."))
                            {
                                if (Path.GetExtension(filePath) == whiteListItem.Substring(1))
                                {
                                    //we got a match
                                    ParseFile(_settings.ProjectDirectory, filePath, templateItems);
                                    break;
                                }
                            }
                            else //a file, like myfile.js
                            {
                                if (Path.GetFileName(filePath) == whiteListItem)
                                {
                                    //we got a match
                                    ParseFile(_settings.ProjectDirectory, filePath, templateItems);
                                    break;
                                }
                            }
                        }

                    }
                }
            }

            return templateItems;
        }

        private void ParseFile(string projectDirectory, string filePath, ConcurrentDictionary<string, TemplateItem> templateItems)
        {
            var referencePath = PathNormalizer.MakeRelativePath(projectDirectory, filePath);

            DebugHelpers.WriteLine("FileNuggetFinder.ParseFile -- {0}", filePath);
           // Lookup any/all nuggets in the file and for each add a new template item.
            using (var fs = File.OpenText(filePath))
            {
                _nuggetParser.ParseString(fs.ReadToEnd(), delegate(string nuggetString, int pos, Nugget nugget, string i_entity)
                {
                    var referenceContext = _settings.DisableReferences
                        ? ReferenceContext.Create("Disabled references", i_entity, 0)
                        : ReferenceContext.Create(referencePath, i_entity, pos);
                    var fileName = Path.GetFileNameWithoutExtension(filePath);
                    // If we have a file like "myfile.aspx.vb" then the fileName will be "myfile.aspx" resulting in split
                    // .pot files. So remove all extensions, so that we just have the actual name to deal with.
                    fileName = fileName.IndexOf('.') > -1 ? fileName.Split('.')[0] : fileName;

                    AddNewTemplateItem(
                        fileName,
                        referenceContext,
                        nugget, 
                        templateItems);
                   // Done.
                    return null; // null means we are not modifying the entity.
                });
            }
        }

        private void AddNewTemplateItem(
            string fileName,
            ReferenceContext referenceContext,
            Nugget nugget, 
            ConcurrentDictionary<string, TemplateItem> templateItems)
        {
            string msgid = nugget.MsgId.Replace("\r\n", "\n").Replace("\r", "\\n");
                // NB: In memory msgids are normalized so that LFs are converted to "\n" char sequence.
            string key = TemplateItem.KeyFromMsgidAndComment(msgid, nugget.Comment, _settings.MessageContextEnabledFromComment);
            List<string> tmpList;
           //
            templateItems.AddOrUpdate(
                key,
                // Add routine.
                k => {
                    TemplateItem item = new TemplateItem();
                    item.MsgKey = key;
                    item.MsgId = msgid;
                    item.FileName = fileName;

                    item.References = new List<ReferenceContext> {referenceContext};

                    if (nugget.Comment.IsSet()) {
                        tmpList = new List<string>();
                        tmpList.Add(nugget.Comment);
                        item.Comments = tmpList;
                    }

                    return item;
                },
                // Update routine.
                (k, v) =>
                {
                    if (!_settings.DisableReferences)
                    {
                        var newReferences = new List<ReferenceContext>(v.References.ToList());
                        newReferences.Add(referenceContext);
                        v.References = newReferences;
                    }

                    if (nugget.Comment.IsSet()) {
                        tmpList = v.Comments != null ? v.Comments.ToList() : new List<string>();
                        if (!_settings.DisableReferences || !tmpList.Contains(nugget.Comment))
                            tmpList.Add(nugget.Comment);
                        v.Comments = tmpList;
                    }

                    return v;
                });
        }
    }
}
