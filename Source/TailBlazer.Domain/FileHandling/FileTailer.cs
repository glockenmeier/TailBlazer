﻿using System;
using System.IO;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using DynamicData;

namespace TailBlazer.Domain.FileHandling
{
    public class FileTailer: IDisposable
    {
        private readonly IDisposable _cleanUp;

        public IObservable<int> TotalLines { get;  }

        public IObservable<int[]> MatchedLines { get; }

        public IObservableList<Line> Lines { get; }

        public FileTailer(FileInfo file, 
            IObservable<string> textToMatch,
            IObservable<ScrollRequest> scrollRequest)
        {
            if (file == null) throw new ArgumentNullException(nameof(file));
            if (textToMatch == null) throw new ArgumentNullException(nameof(textToMatch));
            
            //create list of lines which contain the observable text
            MatchedLines = file.ScanLineNumbers(textToMatch).Replay(1).RefCount();

            //count of lines
            TotalLines = file.CountLines();

            var lines = new SourceList<Line>();
            Lines = lines.AsObservableList();
            
            //this is the beast! Dynamically combine lines requested by the consumer 
            //with the lines which exist in the file. This enables proper virtualisation of the file 
            var scroller = MatchedLines
                .CombineLatest(scrollRequest, (matched, request) => new {allLines = matched , request })
                .Subscribe(x =>
                {
                    var mode = x.request.Mode;
                    var pageSize = x.request.PageSize;
                    var allLines = x.allLines;
                    var currentPage = lines.Items.Select(l => l.Number).ToArray();

                    //If tailing, take the end only. 
                    //Otherwise take the page size and start index from the request
                    var newPage = (mode == ScrollingMode.Tail
                        ? allLines.Skip(pageSize).ToArray()
                        : allLines.Skip(x.request.FirstIndex).Take(pageSize)).ToArray();


                    var added = newPage.Except(currentPage).ToArray();
                    var removed = currentPage.Except(newPage);
                   
                    //read new lines frome the file
                    var addedLines = file.ReadLines(added);
                    //get old lines from the current collection
                    var removedLines = lines.Items.Where(l=> removed.Contains(l.Number)).ToArray();

                    //finally relect changes in the list
                    lines.Edit(innerList =>
                    {
                        innerList.RemoveMany(removedLines);
                        innerList.AddRange(addedLines);
                    });
                });
            _cleanUp = new CompositeDisposable(Lines, scroller, lines);
        }

        public void Dispose()
        {
            _cleanUp.Dispose();
        }
    }
}
