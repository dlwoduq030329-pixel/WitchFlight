using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml;
using System.Xml.Linq;

// Reads exported log cells from every XLSX worksheet, without Excel/COM or new packages.
public static class BattleMovementSpreadsheet
{
    public static IEnumerable<string> Cells(string path)
    {
        using var file=File.OpenRead(path);
        using var zip=new ZipArchive(file,ZipArchiveMode.Read);
        long expanded=0;
        foreach(var entry in zip.Entries)
        {
            expanded+=entry.Length;
            if(expanded>128L*1024*1024) throw new InvalidDataException("엑셀의 압축 해제 크기가 128 MB를 넘습니다. 파일을 나눠 주세요.");
        }
        XDocument Read(ZipArchiveEntry entry)
        {
            using var stream=entry.Open();
            using var reader=XmlReader.Create(stream,new XmlReaderSettings {
                DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=64L*1024*1024 });
            return XDocument.Load(reader);
        }
        string Text(XElement element)=>string.Concat(element.Descendants().Where(x=>x.Name.LocalName=="t").Select(x=>x.Value));
        var shared=new List<string>();
        var strings=zip.GetEntry("xl/sharedStrings.xml");
        if(strings!=null)
            foreach(var item in Read(strings).Descendants().Where(x=>x.Name.LocalName=="si")) shared.Add(Text(item));
        foreach(var sheet in zip.Entries.Where(x=>x.FullName.StartsWith("xl/worksheets/",StringComparison.Ordinal) &&
            x.FullName.EndsWith(".xml",StringComparison.OrdinalIgnoreCase)))
        {
            foreach(var cell in Read(sheet).Descendants().Where(x=>x.Name.LocalName=="c"))
            {
                string kind=(string)cell.Attribute("t");
                string value=cell.Elements().FirstOrDefault(x=>x.Name.LocalName=="v")?.Value ?? "";
                if(kind=="s")
                {
                    if(int.TryParse(value,out int index) && index>=0 && index<shared.Count) yield return shared[index];
                }
                else if(kind=="inlineStr") yield return Text(cell);
                else if(!string.IsNullOrEmpty(value)) yield return value;
            }
        }
    }
}
