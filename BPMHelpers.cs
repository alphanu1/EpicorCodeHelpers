/*Update Ice.UD100*/
/*ttInvTrans recs are deleted in post proc*/
/*If lot x-fered to EXP from any bin, flip to S. If lot x-fered from EXP bin to REL,Change back to I */

foreach(var row in ttInvTrans.Where(tt=>tt.Deleted()))
{
  using (var txscope1 = IceDataContext.CreateDefaultTransactionScope())
  {
    //find UD100 rec
    var ud100 = Db.UD100.Where(u => u.Company == row.Company
                    && u.Key1 == row.ToLotNumber
                    && u.Key2 == row.PartNum
                    ).FirstOrDefault();
    
    if(ud100 != null) 
    {
      var part = Db.Part.Where(p => p.Company == ud100.Company
                               && p.PartNum == ud100.Key2
                               && p.ShortChar01 == "Meniscus" //just keep at Meniscus for now
                               ).FirstOrDefault();
      
      if(part!= null && row.ToBinNum == "EXP")  
      {
        //flip to S if moving to EXP bin
        ud100.Character03 = "S";
      }
      if(part != null && row.ToBinNum == "REL" && row.FromBinNum == "EXP")
      {
        //flip back to I if moving from EXP to REL
        ud100.Character03 = "I";   
      }
      
      Db.Validate();
       txscope1.Complete();
    }    
  }
}

//Find Table Record
Erp.Tables.RMADisp RMADisp;

RMADisp = (from RMADisp_row in Db.RMADisp
    where RMADisp_row.Company == Session.CompanyID
	&& RMADisp_row.RMANum == callContextBpmData.Number01
	&& RMADisp_row.RMALine == callContextBpmData.Number02
    select RMADisp_row).FirstOrDefault();

//Set up an iterator to loop thru results of query
Erp.Tables.OrderDtl OrderDtl;
Erp.Tables.QuoteDtl QuoteDtl;

int LineNum = 1; 
if (orderNum != 0)
{
foreach (var QuoteDtl_iterator in 
	(from QuoteDtl_Row in Db.QuoteDtl
   where QuoteDtl_Row.Company == Session.CompanyID && 
	 QuoteDtl_Row.QuoteNum == callContextBpmData.Number01
	 select QuoteDtl_Row))
 
	{
		QuoteDtl = QuoteDtl_iterator;
    OrderDtl = (from OrderDtl_Row in Db.OrderDtl
    where OrderDtl_Row.Company == Session.CompanyID &&
    OrderDtl_Row.OrderNum == orderNum &&
    OrderDtl_Row.OrderLine == LineNum
    select OrderDtl_Row).FirstOrDefault();

        if (OrderDtl != null)
        {
            OrderDtl["DrawNumFromQuote_c"] = (string)QuoteDtl["DrawNum"];
            //Db.OrderDtl.Update(OrderDtl);
						LineNum++;
        }
	
   }
}



/*Join several tables with Linq*/
/*Send Follow Up Reminder if QuoteHed.FollowUpDate = Today*/
/*Dictionary queue holds to-be-send emails*/
Dictionary<int, object[]> queue = new Dictionary<int, object[]>();

foreach(var quote in (from q in Db.QuoteHed.With(LockHint.NoLock)
                              
                    /*Join QSalesRp, get SalesRepCode*/
                    join r in Db.QSalesRP.With(LockHint.NoLock)
                      on new { q.Company, q.QuoteNum } equals new { r.Company, r.QuoteNum }
                      
                    /*Join SalesRp to QSalesRP, get EmailAddress*/
                    join s in Db.SalesRep.With(LockHint.NoLock)
                      on new {r.Company, r.SalesRepCode} equals new {s.Company, s.SalesRepCode}
                      
                    /*Join Customer to QuoteHed, get Name*/
                    join c in Db.Customer.With(LockHint.NoLock)
                      on new {q.Company, q.CustNum} equals new {c.Company, c.CustNum}
                      
                    where
                    q.Company == Session.CompanyID 
                    && q.FollowUpDate == DateTime.Today
                    && r.PrimeRep == true
                   
                    select new {
                      q.QuoteNum, q.FollowUpDate, s.EMailAddress, c.Name
                      }
                    ))
                    /*Start Iteration Action*/
                    {
                      if(quote != null)
                      {
                        queue.Add(quote.QuoteNum, new object[] {quote.EMailAddress, quote.FollowUpDate, quote.Name});
                      }
                    }
                    /*End Interation Action*/
/*If queue has any entries, we will create a Epicor Mailer class and send out emails to that person*/        
/*Value is an object array. Access contents by index*/
if(queue.Any())
{
  foreach(var row in queue)
  {
    //debug testing
    /*string path = @"\\<server>\C$\Temp\DictionaryContents.txt"; 
    string content = string.Format("Key: {0}, Value: {1}", row.Key, row.Value);
     if(!File.Exists(path))
     {
     // Create a file to write to.
      string createText = "First Line:" + Environment.NewLine;
    File.WriteAllText(path, createText);
     }
     string appendText = Environment.NewLine + "Response: "+DateTime.Now.ToString() + Environment.NewLine + content;
     File.AppendAllText(path, appendText); 
     */
     //Mailer Helpers
     var mailer = this.GetMailer(async: true); 
     var message = new Ice.Mail.SmtpMail();
     message.SetFrom("alerts@jrfortho.org");
     message.SetTo(row.Value[0].ToString());
     //message.SetCC()
     //message.SetBcc() 
     
     DateTime rawDate = (DateTime)row.Value[1];
     var trimDate = rawDate.ToShortDateString();
     string rawBody = string.Format("Quote Follow Up Date set for {0} has been reached for Quote {1} for Customer {2}.", trimDate,row.Key.ToString(), row.Value[2].ToString());
     string htmlWrapperStart = "<html><body><p>";
     string htmlWrapperEnd = "</p></body></html>";
     string finalBody = string.Format("{0}{1}{2}", htmlWrapperStart, rawBody, htmlWrapperEnd);
     
     message.SetBody(finalBody);
     message.Subject = string.Format("Follow Up Reminder: Quote {0}", row.Key.ToString());
     //Dictionary<string, string> attachments = new Dictionary<string, string>();
     //attachments.Add("MyFirstAttachment", @"\\MyServer\myFolder\MyFile.pdf");
     mailer.Send(message);
  }
  queue.Clear();
}   
else
{
  /*
  //debug testing
  string path = @"\\<server>\C$\Temp\BPMDebugger.txt";
  string content = string.Format("No Dictionary Values. DateVar = {0}", DateTime.Today.ToString());
   if(!File.Exists(path))
   {
   // Create a file to write to.
    string createText = "First Line:" + Environment.NewLine;
  File.WriteAllText(path, createText);
   }
   string appendText = Environment.NewLine + "Response: "+DateTime.Now.ToString() + Environment.NewLine + content;
   File.AppendAllText(path, appendText); 
 */
} 
