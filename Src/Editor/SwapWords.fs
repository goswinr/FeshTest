﻿namespace Seff.Editor

open System
open Seff.Model
open ICSharpCode.AvalonEdit
open ICSharpCode.AvalonEdit.Editing
open ICSharpCode.AvalonEdit.Document
open Seff.Util
open System.Windows
open ICSharpCode.AvalonEdit
open ICSharpCode.AvalonEdit
open System.Text

module SwapWords =     

    /// swap currently selected word with word bofore on same line 
    /// a word may contain lettre digit, underscore and dot
    let left(ed:TextEditor) = 
        match Selection.isOneWord ed with 
        |None -> false
        |Some seg -> 
            let doc = ed.Document 
            //ISeffLog.log.PrintfnDebugMsg "seg Word: %s" (doc.GetText(seg))            
            let rec getPrevEnd i = 
                if i<0 then -98 // start of file
                else 
                    let c = doc.GetCharAt i
                    if  Selection.isFsLetter  c then i
                    elif c = '\n' then -99
                    else getPrevEnd (i-1) 
            let prevEnd = getPrevEnd (seg.StartOffset - 1)
            if prevEnd < 0 then false // line end found or start of file found
            else
                let rec getPrevStart i = 
                    if   i<0 then 0 // start of file
                    elif not(Selection.isFsLetter(doc.GetCharAt i)) then i+1
                    else getPrevStart (i-1) 
                let prevStart = getPrevStart prevEnd
                let len = prevEnd-prevStart+1
                let prevWord = doc.GetText(prevStart, len)
                let thisWord = doc.GetText(seg)
                doc.BeginUpdate()
                doc.Replace(seg,prevWord)
                doc.Replace(prevStart, len,thisWord)
                ed.Select(prevStart,thisWord.Length)
                doc.EndUpdate()
                //ISeffLog.log.PrintfnDebugMsg "swap '%s' with '%s'" prevWord thisWord
                true
                
              
    /// swap currently selected word with word afterwords on same line 
    /// a word may contain lettre digit, underscore and dot
    let right (ed:TextEditor) = 
        match Selection.isOneWord ed with 
        |None -> false
        |Some seg -> 
            let doc = ed.Document
            //ISeffLog.log.PrintfnDebugMsg "seg Word: %s" (doc.GetText(seg)) 
            let rec getNextStart i = 
                if i=doc.TextLength then -79 // end of file
                else 
                    let c = doc.GetCharAt i
                    if  Selection.isFsLetter  c then i
                    elif c = '\n'  then -69
                    else getNextStart (i+1) 
            let nextStart = getNextStart (seg.EndOffset + 1)
            if nextStart < 0 then false // line or filr end found
            else
                let rec getNextEnd i = 
                    if  i=doc.TextLength then i-1
                    elif not(Selection.isFsLetter(doc.GetCharAt i)) then i-1
                    else getNextEnd (i+1) 
                let nextEnd = getNextEnd nextStart
                let len = nextEnd-nextStart+1
                let nextWord = doc.GetText(nextStart, len)
                let thisWord = doc.GetText(seg)
                doc.BeginUpdate()
                doc.Replace(nextStart, len,thisWord)
                doc.Replace(seg,nextWord)
                ed.Select(nextStart + nextWord.Length - thisWord.Length,thisWord.Length)
                doc.EndUpdate()
                //ISeffLog.log.PrintfnDebugMsg "swap '%s' with '%s'" prevWord thisWord
                true