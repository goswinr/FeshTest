﻿namespace Seff.Editor

open System.Windows.Input
open System.Threading
open System.Collections.Generic
open FSharp.Compiler.SourceCodeServices
open ICSharpCode.AvalonEdit.Folding
open ICSharpCode.AvalonEdit
open ICSharpCode.AvalonEdit.CodeCompletion
open ICSharpCode.AvalonEdit.Document
open Seff.Util.General
open Seff.Util.String
open Seff.Editor.CodeCompletion
open Seff.Editor


module EditorUtil=

    let currentLine(ed:Editor)=
        let doc = ed.AvaEdit.Document
        doc.GetText(doc.GetLineByOffset(ed.AvaEdit.CaretOffset))
    
    let currentLineBeforeCaret(ed: Editor)=
        let doc = ed.AvaEdit.Document
        let car = ed.AvaEdit.TextArea.Caret
        let caretOffset = car.Offset
        let ln = doc.GetLineByOffset(caretOffset)
        let caretOffsetInThisLine = caretOffset - ln.Offset
        
        { lineToCaret = doc.GetText(ln.Offset, caretOffsetInThisLine) 
          row =    car.Line  
          column = caretOffsetInThisLine // equal to amount of characters in lineToCaret
          offset = caretOffset }


module FsService = 

    type TextChange =  EnteredDot | EnteredOneIdentifierChar | EnteredOneNonIdentifierChar | CompletionWinClosed | TabChanged | OtherChange //| EnteredQuote
    type CharBeforeQuery = Dot | NotDot
    
    let keywordsComletionLines = [| 
        for kw,desc in Keywords.KeywordsWithDescription  do 
            yield CompletionLineKeyWord(kw,desc) :> ICompletionData
            yield CompletionLineKeyWord("__SOURCE_DIRECTORY__","Evaluates to the current full path of the source directory" ) :> ICompletionData    
            yield CompletionLineKeyWord("__SOURCE_FILE__"     ,"Evaluates to the current source file name, without its path") :> ICompletionData    
            yield CompletionLineKeyWord("__LINE__",            "Evaluates to the current line number") :> ICompletionData    
            |]
    let Keywords = Keywords.KeywordsWithDescription |> List.map fst |> HashSet

    // to be able to cancel all running FSC checker threads when text changed (there should only be one)
    let FsCheckerCancellationSources = new System.Collections.Concurrent.ConcurrentDictionary<int,CancellationTokenSource>()
    

    let inline cleartoken(checkerId) =
        let ok,_ = FsCheckerCancellationSources.TryRemove(checkerId)
        if not ok && checkerId<> 0 then printfn "Failed to remove token '%d' from  FsCheckerCancellationSources" checkerId //TODO use log
        ()

    let checkForErrorsAndUpdateFoldings (ed:Editor, checkDone : Option<FsCheckResults> ) = 
        //log.Print "*checkForErrorsAndUpdateFoldings..."
        
        let mutable checkerId = 0 

        let updateFoldings () =             
            cleartoken(checkerId)
            if ed.FsCheckerId = checkerId then 
                if ed.Foldings.IsSome && notNull ed.FoldingManager && ed.IsCurrent then 
                    let foldings=ResizeArray<NewFolding>()
                    for st,en in ed.Foldings.Value do foldings.Add(NewFolding(st,en)) //if new folding type is created async a waiting symbol apears on top of it 
                    let firstErrorOffset = -1 //The first position of a parse error. Existing foldings starting after this offset will be kept even if they don't appear in newFoldings. Use -1 for this parameter if there were no parse errors)                    
                    ed.FoldingManager.UpdateFoldings(foldings,firstErrorOffset)
                ed.FsCheckerId <- 0

        let highlightErrors (chr:FsCheckResults) = 
            cleartoken(checkerId)
            async{ 
                do! Async.SwitchToContext Sync.syncContext                
                if ed.FsCheckerId = checkerId && chr.ok && ed.IsCurrent then
                    ed.FsCheckerResult <- Some chr.checkRes // cache for type info
                    ed.ErrorMarker.Clear()
                    match chr.checkRes.Errors with 
                    | [| |] -> 
                        //ed.Editor.Background <- Appearance.editorBackgroundOk
                        StatusBar.SetErrors ([| |])
                    | es   -> 
                        //ed.Editor.Background <- Appearance.editorBackgroundErr
                        StatusBar.SetErrors (es)
                        ed.ErrorMarker.AddSegments(es)
                        Packages.checkForMissingPackage tab e startOffset length

                do! Async.Sleep 500  
                if ed.FsCheckerId = checkerId && ed.IsCurrent then // another checker migh alredy be started
                    checkerId <- rand.Next()  
                    ed.FsCheckerId <- checkerId
                    let cancelFoldScr = new CancellationTokenSource()
                    if not <| FsCheckerCancellationSources.TryAdd(checkerId,cancelFoldScr) then log.Print "Failed to collect FsFolderCancellationSources" 
                    Async.StartWithContinuations(
                            FsFolding.get(tab,chr.code),
                            updateFoldings,
                            (fun ex   -> log.Print "Error in Async updateFoldings") ,
                            (fun cncl -> () ), //log.Print "Async updateFoldings cancelled"),
                            cancelFoldScr.Token)
                
                }|> Async.StartImmediate            
        
        match checkDone with 
        |Some chr ->             
            highlightErrors (chr)        
        |None ->
            checkerId <- rand.Next()  
            ed.FsCheckerId <- checkerId                 
            async{  
                do! Async.Sleep 200               
                if ed.FsCheckerId = checkerId &&  ed.IsCurrent then
                    let cancelScr = new CancellationTokenSource()
                    if not <| FsCheckerCancellationSources.TryAdd(checkerId,cancelScr) then log.Print "Failed to add FsCheckerCancellationSources" 
            
                    Async.StartWithContinuations(
                            FsChecker.checkAndIndicate (tab, 0 , checkerId),
                            highlightErrors,
                            (fun ex   -> log.Print "Error in FsChecker.check") ,
                            (fun cncl -> ()), //log.Print "FsChecker.check cancelled"),
                            cancelScr.Token)
                }|> Async.StartImmediate 
        

    let prepareAndShowComplWin(tab:Tab, pos:FsChecker.PositionInCode , changetype, setback, query, charBefore, onlyDU) = 
        //log.Print "*prepareAndShowComplWin..."
        if changetype = EnteredOneIdentifierChar  &&  Keywords.Contains query  then //this never happens since typing complete word happens in when window is open not closed
            // do not complete, if keyword was typed full, just continue typing, completion will triger anyway on additional chars
            checkForErrorsAndUpdateFoldings(tab,None)
            
        else
            //let prevCursor = ed.Editor.Cursor
            //ed.Editor.Cursor <- Cursors.Wait //TODO does this get stuck on folding collumn ?

            let aComp = 
                async{
                    let! chr =  FsChecker.checkAndIndicate (tab,  pos.offset, 0)
                    if chr.ok && ed.IsCurrent then                        
                        //log.Print "*2-prepareAndShowComplWin geting completions"
                        let ifDotSetback = if charBefore = Dot then setback else 0
                        let! decls     = FsChecker.getDeclListInfo    (chr.parseRes , chr.checkRes , pos , ifDotSetback) //TODO, can this be avoided use info from below symbol call ?
                        
                        //find optional arguments too:
                        let! declSymbs = FsChecker.getDeclListSymbols (chr.parseRes , chr.checkRes , pos , ifDotSetback) // only for optional parmeter info ?
                        let optArgDict = Dictionary()
                        for symbs in declSymbs do 
                            for symb in symbs do 
                                let opts = Tooltips.infoAboutOptinals symb
                                if opts.Count>0 then 
                                    optArgDict.[symb.Symbol.FullName]<- opts

                        do! Async.SwitchToContext Sync.syncContext

                        let completionLines = ResizeArray<ICompletionData>()                                
                        if not onlyDU && charBefore = NotDot then   completionLines.AddRange keywordsComletionLines  // add keywords to list
                        for it in decls.Items do
                            
                            match it.Glyph with 
                            |FSharpGlyph.Union|FSharpGlyph.Module | FSharpGlyph.EnumMember -> completionLines.Add (new CompletionLine(it, (changetype = EnteredDot), optArgDict)) 
                            | _ -> if not onlyDU then                                         completionLines.Add (new CompletionLine(it, (changetype = EnteredDot), optArgDict))
                        

                        //ed.Editor.Cursor <- prevCursor
                        if ed.IsCurrent then
                            //log.Print "*prepareAndShowComplWin for '%s' with offset %d" pos.lineToCaret setback                            
                            if completionLines.Count > 0 then showCompletionWindow(tab, completionLines, setback, query)
                            else checkForErrorsAndUpdateFoldings(tab, Some chr)
                } 
            
            let checkerId = rand.Next()  
            ed.FsCheckerId <- checkerId 
            let cancelScr = new CancellationTokenSource()
            if not <| FsCheckerCancellationSources.TryAdd(checkerId,cancelScr) then log.PrintAppErrorMsg "Failed to collect FsCheckerCancellationSources"
            Async.StartImmediate(aComp, cancelScr.Token)   
    
    let textChanged (change:TextChange ,tab:Tab) =
        log.PrintDebugMsg "*1-textChanged because of %A" change 
        match ed.CompletionWin with
        | Some w ->  
            if w.CompletionList.ListBox.HasItems then 
                //TODO check text is full mtch and close completion window ?
                () // just keep on tying in completion window, no type checking !
            else 
                w.Close() 

        | None -> //no completion window open , do type check..

            // first cancel all previous checker threads  //TODO do thids only if new checker is started ?? 
            for checkId in FsCheckerCancellationSources.Keys do
                let ok,toCancel = FsCheckerCancellationSources.TryRemove(checkId)
                if ok then 
                    //log.Print "checker thread cancelled" // does never print, why // it does print !!
                    toCancel.Cancel()
                else
                    log.Print "Failed get checkId from FsCheckerCancellationSources" 
            
            match change with             
            | OtherChange | CompletionWinClosed | TabChanged  | EnteredOneNonIdentifierChar -> //TODO maybe do less call to error highlighter when typing in string or comment ?
                checkForErrorsAndUpdateFoldings(tab,None) 

            | EnteredOneIdentifierChar | EnteredDot -> 

                let pos = EditorUtil.currentLineBeforeCaret tab // this line will include the charcater that trigger auto completion(dot or first letter)
                let line = pos.lineToCaret
                
                //possible cases where autocompletion is not desired:
                //let isNotInString           = (countChar '"' line ) - (countSubString "\\\"" line) |> isEven && not <| line.Contains "print" // "\\\"" to ignore escaped quotes of form \" ; check if formating string
                let isNotAlreadyInComment   = countSubString "//"  line = 0  ||  lastCharIs '/' line   // to make sure comment was not just typed(then still check)
                let isNotLetDecl            = let lk = (countSubString "let " line) + (countSubString "let(" line) in lk <= (countSubString "=" line) || lk <= (countSubString ":" line)
                // TODO add check for "for" declaration
                let isNotFunDecl            = let fk = (countSubString "fun " line) + (countSubString "fun(" line) in fk <= (countSubString "->" line)|| fk <= (countSubString ":" line)
                let doCompletionInPattern, onlyDU   =  
                    match stringAfterLast " |" (" "+line) with // add starting step to not fail at start of line with "|"
                    |None    -> true,false 
                    |Some "" -> log.Print " this schould never happen since we get here only with letters, but not typing '|'" ; false,false // most comen case: '|" was just typed, next pattern declaration starts after next car
                    |Some s  -> 
                        let doCompl = 
                            s.Contains "->"             || // name binding already happend 
                            isOperator s.[0]            || // not in pattern matching 
                            s.[0]=']'                   || // not in pattern matching 
                            (s.Contains " :?" && not <| s.Contains " as ")  // auto complete desired  after '| :?" type check but not after 'as' 
                        if not doCompl && startsWithUppercaseAfterWhitespace s then // do autocomplete on DU types when starting with uppercase Letter
                           if s.Contains "(" || s.Contains " " then   false,false //no completion binding a new name inside a DU
                           else                                       true ,true //upper case only, show DU and Enum in completion list, if all others are false
                        else
                           doCompl,false //not upper case, other 3 decide if anything is shown

                //log.Print "isNotInString:%b; isNotAlreadyInComment:%b; isNotFunDeclaration:%b; isNotLetDeclaration:%b; doCompletionInPattern:%b, onlyDU:%b" isNotInString isNotAlreadyInComment isNotFunDecl isNotLetDecl doCompletionInPattern onlyDU
            
                if (*isNotInString &&*) isNotAlreadyInComment && isNotFunDecl && isNotLetDecl && doCompletionInPattern then
                    let setback     = lastNonFSharpNameCharPosition line                
                    let query       = line.Substring(line.Length - setback)
                    let isKeyword   = Keywords.Contains query
                    //log.Print "pos:%A setback='%d'" pos setback
                
                                       
                    let charBeforeQueryDU = 
                        let i = pos.column - setback - 1
                        if i >= 0 && i < line.Length then 
                            if line.[i] = '.' then Dot else NotDot
                        else
                            NotDot

                    

                    if charBeforeQueryDU = NotDot && isKeyword then
                        //log.Print "*2.1-textChanged highlighting with: query='%s', charBefore='%A', isKey=%b, setback='%d', line='%s' " query charBeforeQueryDU isKeyword setback line
                        checkForErrorsAndUpdateFoldings(tab,None)

                    else 
                        //log.Print "*2.2-textChanged Completion window opening with: query='%s', charBefore='%A', isKey=%b, setback='%d', line='%s' change=%A" query charBeforeQueryDU isKeyword setback line change
                        prepareAndShowComplWin(tab, pos, change, setback, query, charBeforeQueryDU, onlyDU)
                else
                    //checkForErrorsAndUpdateFoldings(tab)
                    //log.Print "*2.3-textChanged didn't trigger of checker not needed? \r\n"
                    ()
    