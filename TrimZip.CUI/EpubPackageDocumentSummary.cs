using System;
using System.Collections.Generic;
using System.Linq;

namespace TrimZip.CUI
{
    internal sealed class EpubPackageDocumentSummary
    {
        public EpubPackageDocumentSummary((string Name, string? FileAs) title, IEnumerable<(string Name, string? FileAs, string? Role, string? RoleScheme, int? DisplaySeq)> creators, (string Name, string? FileAs)? publisher, string language, DateTimeOffset? modified, IEnumerable<string> subjects, string? description)
        {
            Title = title;
            Creators = creators.ToList();
            Publisher = publisher;
            Language = language;
            Modified = modified;
            Subjects = subjects;
            Description = description;
        }

        public (string Name, string? FileAs) Title { get; }
        public IEnumerable<(string Name, string? FileAs, string? Role, string? RoleScheme, int? DisplaySeq)> Creators { get; }
        public (string Name, string? FileAs)? Publisher { get; }
        public string Language { get; }
        public DateTimeOffset? Modified { get; }
        public IEnumerable<string> Subjects { get; }
        public string? Description { get; }
    }
}
