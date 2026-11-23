using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

var root=@"C:\Users\kajet\WireMods\WiresAcoustics";
var output=Path.Combine(root,"WA_METHOD_INVENTORY_20261122.tsv");
var blocks=Path.Combine(root,"WA_METHOD_SIGNATURES_20261122.md");
var lines=new List<string>{"File\tLine\tType\tMethod\tSignature\tLOC\tLoops\tBranches\tCalls\tFirstStatements"};
var markdown=new StringBuilder("# Wire's Acoustics 0.7.0 — exhaustive method inventory\n\n");
int total=0;
foreach(var path in Directory.EnumerateFiles(Path.Combine(root,"src"),"*.cs").OrderBy(p=>p)){
    var file=Path.GetFileName(path);
    var content=File.ReadAllText(path);
    var tree=CSharpSyntaxTree.ParseText(content);
    var syntax=tree.GetRoot();
    markdown.AppendLine($"### {file}");
    var entries=new List<(int start,string row,string name)>();
    var all=syntax.DescendantNodes().Where(node=>node is MethodDeclarationSyntax
        || node is ConstructorDeclarationSyntax || node is DestructorDeclarationSyntax
        || node is OperatorDeclarationSyntax || node is ConversionOperatorDeclarationSyntax
        || node is LocalFunctionStatementSyntax
        || node is AccessorDeclarationSyntax && (((AccessorDeclarationSyntax)node).Body!=null || ((AccessorDeclarationSyntax)node).ExpressionBody!=null)
        || node is PropertyDeclarationSyntax p && p.ExpressionBody!=null
        || node is IndexerDeclarationSyntax i && i.ExpressionBody!=null);
    foreach(var node in all){
        var span=tree.GetLineSpan(node.Span);
        var start=span.StartLinePosition.Line+1;
        var end=span.EndLinePosition.Line+1;
        var parent=node.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault();
        var type=parent?.Identifier.Text??"(local)";
        string signature=node switch{
            MethodDeclarationSyntax m => m.ReturnType+" "+m.Identifier+m.TypeParameterList+"("+m.ParameterList.Parameters+")",
            ConstructorDeclarationSyntax c=>c.Identifier+"("+c.ParameterList.Parameters+")",
            LocalFunctionStatementSyntax l=>l.ReturnType+" "+l.Identifier+"("+l.ParameterList.Parameters+")",
            AccessorDeclarationSyntax ac => (ac.Parent?.Parent as PropertyDeclarationSyntax)?.Identifier.Text+"."+ac.Keyword.Text,
            PropertyDeclarationSyntax prop=>prop.Type+" "+prop.Identifier,
            OperatorDeclarationSyntax op => op.ReturnType+" operator "+op.OperatorToken+"("+op.ParameterList.Parameters+")",
            _=> Regex.Replace(node.ToString(),@"\s+"," ").Substring(0,Math.Min(130,node.ToString().Length))
        };
        string name=node switch {
           MethodDeclarationSyntax m=>m.Identifier.Text,
           ConstructorDeclarationSyntax c=>c.Identifier.Text,
           LocalFunctionStatementSyntax l=>l.Identifier.Text+"(local)",
           AccessorDeclarationSyntax ac=>(ac.Parent?.Parent as PropertyDeclarationSyntax)?.Identifier.Text+"."+ac.Keyword.Text,
           PropertyDeclarationSyntax prop=>prop.Identifier.Text+"(property)",
           _=>signature};
        var descendants=node.DescendantNodes().Where(n=>!n.Ancestors().Any(x=>x!=node&&x is LocalFunctionStatementSyntax)).ToList();
        var calls=descendants.OfType<InvocationExpressionSyntax>()
                .Select(x=>Regex.Replace(x.Expression.ToString(),@"\s+"," "))
                .Distinct().Take(16).ToArray();
        var loops=descendants.Count(n=>n is ForStatementSyntax or ForEachStatementSyntax or WhileStatementSyntax or DoStatementSyntax);
        var branches=descendants.Count(n=>n is IfStatementSyntax or SwitchStatementSyntax or ConditionalExpressionSyntax);
        var stmts=descendants.OfType<StatementSyntax>().Where(s=>s.Parent is BlockSyntax
            && s is not BlockSyntax).Take(3).Select(s=>Regex.Replace(s.ToString(),@"\s+"," ").Trim()).ToArray();
        string shortStmts=string.Join(" | ",stmts);
        if(shortStmts.Length>230) shortStmts=shortStmts[..230];
        string safe(string s)=>s.Replace("\t"," ").Replace("\r"," ").Replace("\n"," ");
        var row=string.Join('\t',file,start,type,name,safe(signature),end-start+1,loops,branches,safe(string.Join(", ",calls)),safe(shortStmts));
        entries.Add((start,row,name+" ("+start+", "+(end-start+1)+" lines)"));
        total++;
    }
    foreach(var (start,row,name) in entries.OrderBy(x=>x.start)){lines.Add(row);markdown.AppendLine("- "+(char)96+name+(char)96);}
    markdown.AppendLine();
    Console.WriteLine($"{file,-31} {entries.Count,3} members");
}
File.WriteAllLines(output,lines);
File.WriteAllText(blocks,markdown.ToString());
Console.WriteLine($"METHOD_COUNT={total} INVENTORY={output} INDEX={blocks}");

int lambdas=0;
foreach(var path in Directory.EnumerateFiles(Path.Combine(root,"src"),"*.cs").OrderBy(p=>p)){
    var file=Path.GetFileName(path);
    var tree=CSharpSyntaxTree.ParseText(File.ReadAllText(path));
    foreach(var node in tree.GetRoot().DescendantNodes().Where(n=>
        n is ParenthesizedLambdaExpressionSyntax || n is SimpleLambdaExpressionSyntax || n is AnonymousMethodExpressionSyntax)){
        var position=tree.GetLineSpan(node.Span).StartLinePosition.Line+1;
        var expression=Regex.Replace(node.ToString(),@"\s+"," ");
        if(expression.Length>160) expression=expression[..160];
        Console.WriteLine($"LAMBDA {file}:{position} {expression}");
        lambdas++;
    }
}
Console.WriteLine($"LAMBDA_COUNT={lambdas}");
