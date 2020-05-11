﻿namespace Seff.Config

open Seff
open System
open System.IO
open System.Text
open System.Collections.Generic


type FileToOpen = {file:FileInfo; makeCurrent:bool}

/// files that are open when closing the editor window, for next restart
type OpenTabs  (log:ISeffLog, adl:AppDataLocation, startupArgs:string[]) = 
    let writer = SaveWriter(log)
    
    let filePath = adl.GetFilePath("CurrentlyOpenFiles.txt")

    let currentTabPreFix =  "*Current tab:* " //a string that can never be part of a filename

    let mutable allFiles:seq<FileInfo> = Seq.empty

    let mutable currentFile:FileInfo option = None

    let files = 
        let files = ResizeArray()
        let dup =  HashSet()
        let mutable curr ="" 
        try            
            // parse settings
            if IO.File.Exists filePath then 
                let lns = IO.File.ReadAllLines filePath 
                if lns.Length > 1 then 
                    curr <- lns.[0].Replace(currentTabPreFix,"").ToLowerInvariant() // first line is filepath and name for current tab (repeats below)
                    for path in lns |> Seq.skip 1  do // skip first line of current info
                        let fi = FileInfo(path)
                        if fi.Exists then 
                            files.Add fi 
                            dup.Add (path.ToLowerInvariant())  |> ignore 

            // parse startup args
            for path in startupArgs do
                let fi = FileInfo(path)
                if fi.Exists then 
                    let lc = path.ToLowerInvariant()
                    curr <- lc
                    if not <| dup.Contains (lc) then 
                        dup.Add (lc)  |> ignore
                        files.Add fi
                  
        with e -> 
            log.PrintAppErrorMsg "Error getFilesfileOnClosingOpen: %s"  e.Message
        
        [|  
        for fi in files do 
            let lc = fi.FullName.ToLowerInvariant()
            {file= fi; makeCurrent = lc.Equals(curr, StringComparison.Ordinal)}
        |]

    let getText() = 
        let curr = if currentFile.IsSome then currentTabPreFix + currentFile.Value.FullName else "*No current tab*"
        let sb = StringBuilder()
        sb.AppendLine(curr) |> ignore // first line is filepath and name for current tab (repeats below)
        for f in allFiles do 
            sb.AppendLine(f.FullName) |> ignore   
        sb.ToString()

    member this.Save (currentFileO:FileInfo option , allFilesO: seq<FileInfo>) =         
        currentFile<-currentFileO
        allFiles<-allFilesO
        writer.WriteDelayed  (filePath, getText ,500)
      
    /// second item in tuple indicates current tab
    /// ensures that ther is only one file to make current
    member this.Get() = files
      