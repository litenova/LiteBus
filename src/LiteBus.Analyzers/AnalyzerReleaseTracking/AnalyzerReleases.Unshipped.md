### New Rules

 Rule ID | Category              | Severity | Notes                                              
---------|-----------------------|----------|----------------------------------------------------
 LB1001  | LiteBus.Handlers      | Error    | Duplicate command handler                          
 LB1003  | LiteBus.Handlers      | Warning  | Query handler impurity                             
 LB1004  | LiteBus.Inbox         | Error    | Command with result scheduled to inbox             
 LB1005  | LiteBus.Handlers      | Error    | Unsupported open generic handler                   
 LB1007  | LiteBus.Contracts     | Warning  | Missing message contract registration              
 LB1008  | LiteBus.Handlers      | Error    | Missing command handler                            
 LB1009  | LiteBus.Handlers      | Error    | Missing query handler                              
 LB1010  | LiteBus.Handlers      | Error    | Duplicate query handler                            
 LB1011  | LiteBus.Handlers      | Warning  | Orphan handler tag                                 
 LB1012  | LiteBus.Handlers      | Warning  | Duplicate handler across assemblies                
 LB1013  | LiteBus.Outbox        | Warning  | Transactional outbox without DbContext             
 LB1014  | LiteBus.Configuration | Error    | Processor enabled without dispatcher               
 LB1015  | LiteBus.Configuration | Warning  | Transactional storage without interceptor          
 LB1016  | LiteBus.Inbox         | Warning  | Transactional inbox without DbContext              
 LB1017  | LiteBus.Contracts     | Warning  | Explicit message contract registration recommended 
