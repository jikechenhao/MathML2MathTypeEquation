﻿using System;
using System.Data;
using System.IO;
using Microsoft.Office.Interop;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using MTSDKDN;
using Word = Microsoft.Office.Interop.Word;
using Excel = Microsoft.Office.Interop.Excel;
using IDataObject = System.Runtime.InteropServices.ComTypes.IDataObject;
using System.Text.RegularExpressions;
using System.Linq.Expressions;
using System.Collections.Generic;
using System.Net;

namespace ConvertEquations
{
    class Program
    {
        //用于作为函数的默认参数
        public static object nothing = System.Reflection.Missing.Value;

        public static WebClient webClient = new WebClient();

        //微软提供的可调用的API入口
        [DllImport("shell32.dll ")]
        public static extern int ShellExecute(IntPtr hwnd, String lpszOp, String lpszFile, String lpszParams, String lpszDir, int FsShowCmd);

        //主程序入口，必须以单线程方式启动
        [STAThread]
        static void Main(string[] args)
        {
            Console.Write("请先选择excel文件，是否继续？enter");
            int read = Console.Read();
            if (read != 0)
            {
                Program program = new Program();
                OpenFileDialog fileDialog = new OpenFileDialog();
                fileDialog.Multiselect = true;
                fileDialog.Title = "请选择需要转换的Excel文件";
                fileDialog.Filter = "所有文件(*.xlsx)|*.*";
                string file = "";
                if (fileDialog.ShowDialog() == DialogResult.OK)
                {
                    file = fileDialog.FileName;
                    Console.WriteLine("已选择文件:" + file);
                }
                if (file.EndsWith(".xlsx") || file.EndsWith(".xls"))
                {
                    string savepath = System.Configuration.ConfigurationManager.AppSettings["savepath"];
                    Console.WriteLine("正在读取Excel...");
                    program.MathML2MathTypeWord(program, new ConvertEquation(), savepath, file);
                }
                else
                {
                    MessageBox.Show("请选择正确的excel文件");
                    Application.Exit();
                    return;
                }
                
            }
            else
            {
                Application.Exit();
                return;
            }
        }

        /// <summary>
        /// convert mathml to mathtype equation type into word
        /// </summary>
        /// <param name="p"></param>
        /// <param name="ce"></param>
        /// <param name="savepath">the file where to save</param>
        /// <param name="filename">the input file name</param>
        /// <returns></returns>
        public string MathML2MathTypeWord(Program p, ConvertEquation ce, string savepath, string file)
        {
            Utils.killAllProcess("winword.exe");
            Utils.killAllProcess("mathtype.exe");
            Utils.killAllProcess("excel.exe");

            object name = file.Substring(0, file.LastIndexOf(".")) + ".doc";

            //create document
            Word.Application newapp = new Word.Application();
            //create a word document
            Word.Document newdoc = newapp.Documents.Add(ref nothing, ref nothing, ref nothing, ref nothing);
            //是否显示word程序界面
            newapp.Visible = false;

            Excel.Workbook workbook = null;
            Excel.Worksheet worksheet = null;
           
            Excel.Application excel = new Excel.Application();//lauch excel application   
            if (excel == null)
            {
                return ResultCode.EXCEL_READ_ERROR;
            }
            excel.Visible = false; 
            excel.UserControl = true;
            // 以只读的形式打开EXCEL文件  
            workbook = excel.Application.Workbooks.Open(file, nothing, true, nothing, nothing, nothing,nothing, nothing, nothing, true, nothing, nothing, nothing, nothing, nothing);
            //取得第一个工作薄
            worksheet = (Excel.Worksheet)workbook.Worksheets.get_Item(1);
            //取得总记录行数   (包括标题列)
            int iRowCount = worksheet.UsedRange.Rows.Count;
            int iColCount = worksheet.UsedRange.Columns.Count;
            //生成列头
            List<string> titles = new List<String>();
            for (int i = 0; i < iColCount; i++)
            {
                var txt = ((Excel.Range)worksheet.Cells[1, i + 1]).Text.ToString();
                titles.Add(txt.ToString()+ ": ");

            }
            Console.WriteLine("Excel读取完成...");
            //生成行数据
            Excel.Range range;
            //从第二行开始
            int rowIdx = 2;
            int count = 0;
            object anchor = null;
            List<string> imgs = new List<string>();
            for (int iRow = rowIdx; iRow <= iRowCount; iRow++)
            {
                for (int iCol = 1; iCol <= iColCount; iCol++)
                {
                    //插入列头
                    newapp.Selection.Font.Color = Word.WdColor.wdColorBlue;
                    newapp.Selection.TypeText(titles[iCol - 1]);
                    //得到单元格内容
                    range = (Excel.Range)worksheet.Cells[iRow, iCol];
                    string d = range.Text.ToString();
                    string[] oneLevelData = d.Split(new string[] { "<math", "</math>" }, StringSplitOptions.None);
                    try
                    {
                        newapp.Selection.Font.Color = Word.WdColor.wdColorBlack;
                        foreach (string datas in oneLevelData)
                        {
                            if (datas.StartsWith(" xmlns="))
                            {
                                string mathml = "<math" + datas + "</math>";
                                mathml = MathML.preproccessMathml(mathml);
                                Console.WriteLine("转换公式: " + mathml);
                                ce.Convert(new EquationInputFileText(mathml, ClipboardFormats.cfMML), new EquationOutputClipboardText());
                                count++;
                                WordUtils.moveLeft(newdoc, 1);
                                newapp.Selection.Paste();
                                if (count == 9)
                                {
                                    Utils.killAllProcess("mathtype.exe");
                                    count = 0;
                                }
                            }
                            else
                            {
                                //var html = HTMLUtils.HtmlClipboardData(datas);
                                //HTMLUtils.CopyHTMLToClipboard(html);
                                //object dataType = Word.WdPasteDataType.wdPasteHTML;
                                //newapp.Selection.PasteSpecial(ref nothing, ref nothing, ref nothing, ref nothing, ref dataType, ref nothing, ref nothing);

                                string[] tags = datas.Split(new string[] { "<img", "<IMG" }, StringSplitOptions.None);
                                foreach (string tag in tags)
                                {
                                    //regular expression extract img src resource
                                    string matchString = Regex.Match("<img " + tag, "<img.+?src=[\"'](.+?)[\"'].*?>", RegexOptions.IgnoreCase).Groups[1].Value;
                                    if (matchString != null && !"".Equals(matchString))
                                    {
                                        anchor = newdoc.Application.Selection.Range;
                                        //regular expression extract file name
                                        string imgfilename = Regex.Match(matchString, ".+/(.+)$", RegexOptions.IgnoreCase).Groups[1].Value;
                                        string imgpath = savepath + imgfilename;
                                        imgs.Add(imgpath);
                                        //download the image from the url
                                        webClient.DownloadFile(matchString, imgpath);
                                        //add the picture into word
                                        newdoc.Application.ActiveDocument.InlineShapes.AddPicture(imgpath, true, true, ref anchor);
                                        newapp.Selection.Move();
                                        Console.WriteLine("插入图片: " + imgpath);
                                    }
                                    var newtag = tag;
                                    if (tag != null && (tag.StartsWith(" img_type") || tag.Contains("src")))
                                    {
                                        newtag = "<img " + tag;
                                    }
                                    string text = Utils.NoHTML(newtag);
                                    if (text != null && !"".Equals(text))
                                    {
                                        //去除空格、插入文本b
                                        newapp.Selection.TypeText(text.Trim());
                                        newapp.Selection.Move();
                                        Console.WriteLine("插入文本: " + text);
                                    }
                                }
                            }
                        }
                        newapp.Selection.TypeParagraph();
                    }
                    catch (Exception et)
                    {
                        Console.WriteLine(et);
                    }
                }
                newapp.Selection.TypeParagraph();
                //清空粘贴板，否则会将前一次粘贴记录保留。
                Clipboard.SetDataObject("", true);
            }

            try
            {
                object fileFormat = Word.WdSaveFormat.wdFormatDocument;
                newdoc.SaveAs(ref name, fileFormat, ref nothing, ref nothing, ref nothing, ref nothing, ref nothing,
                       ref nothing, ref nothing, ref nothing, ref nothing, ref nothing, ref nothing, ref nothing,
                       ref nothing, ref nothing);
            }
            catch (Exception ex)
            {
                try
                {
                    newdoc.Close(ref nothing, ref nothing, ref nothing);
                }
                catch (Exception tt)
                {
                    Console.WriteLine(tt);
                }
                Console.WriteLine(ex);
            }
            finally
            {
                excel.Quit();
                newapp.Quit();
                excel = null;
                newdoc = null;
                newapp = null;
                Utils.deleteFile(imgs);
            }
            return ResultCode.SUCCESS;
        }

    }
}
