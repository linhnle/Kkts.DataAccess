"""HTTP acceptance against the local synthetic example. Does not replace browser UI acceptance."""
import http.cookiejar,json,re,urllib.request,urllib.parse,urllib.error
import os
base=os.environ.get('KDA_EXAMPLE_URL','http://127.0.0.1:5189')
def client():return urllib.request.build_opener(urllib.request.HTTPCookieProcessor(http.cookiejar.CookieJar()))
def request(c,path,fields=None):
    data=None if fields is None else urllib.parse.urlencode(fields).encode()
    try:
        with c.open(base+path,data=data) as response:return response.status,response.read().decode()
    except urllib.error.HTTPError as error:return error.code,error.read().decode()
def token(page):return re.search(r"name=['\"]__RequestVerificationToken['\"](?: type=['\"]hidden['\"])? value=['\"]([^'\"]+)",page).group(1)
def login(c,profile):
    status,page=request(c,'/login');assert status==200
    return request(c,'/login',{'profile':profile,'__RequestVerificationToken':token(page)})
admin=client();status,page=login(admin,'1:admin');assert status==200
csrf=token(page)
assert request(admin,'/admin/publish',{})[0]==400
assert request(admin,'/admin/publish',{'__RequestVerificationToken':csrf,'draftRevision':'1','activeRevision':'0'})[0]==200
for tenant in [1,2]:
    if tenant==2:
        tenant_admin=client();_,admin_page=login(tenant_admin,'2:admin')
        assert request(tenant_admin,'/admin/publish',{'__RequestVerificationToken':token(admin_page),'draftRevision':'1','activeRevision':'0'})[0]==200
    for role,expected in [('manager',2),('hr',3),('employee',1),('none',None)]:
        user=client();status,result=login(user,f'{tenant}:{role}')
        if expected is None:assert status==403;continue
        assert status==200,(role,status,result)
        data=json.loads(result);assert data['total']==expected
        assert all(set(r)=={'Id','Name','Email'} for r in data['records'])
        assert all(r['Id']//100==tenant for r in data['records'])
        assert request(user,'/admin')[0]==403
        assert request(user,f'/employees/{(3-tenant)*100+1}')[0]==404
        assert request(user,'/employees/999')[0]==404
print('Employee HTTP scenarios passed: two tenants, manager/HR/employee/no-grant, hidden fields, detail equivalence, admin denial, request protection.')
from pathlib import Path
payload=Path('docs/employee-policy.json').read_text()
status,page=request(admin,'/admin');csrf=token(page)
preview_fields={'__RequestVerificationToken':csrf,'payload':payload,'sampleRole':'manager'}
status,result=request(admin,'/admin/preview',preview_fields);assert status==200
preview=json.loads(result);assert preview['label']=='Synthetic fixtures only'
assert sum(r['allowed'] for r in preview['records'])==2
assert 'manager-active-department' in preview['applicableRules']
assert request(admin,'/admin/save',dict(preview_fields,draftRevision='0'))[0]==409
status,page=request(admin,'/admin/save',dict(preview_fields,draftRevision='1'));assert status==200
csrf=token(page)
assert request(admin,'/admin/publish',{'__RequestVerificationToken':csrf,'draftRevision':'1','activeRevision':'1'})[0]==409
status,page=request(admin,'/admin/publish',{'__RequestVerificationToken':csrf,'draftRevision':'2','activeRevision':'1'});assert status==200
csrf=token(page)
assert request(admin,'/admin/rollback',{'__RequestVerificationToken':csrf,'version':'1','activeRevision':'2'})[0]==200
user=client();login(user,'1:manager');_,login_page=request(user,'/login');csrf=token(login_page)
def post_json_for(c,path,payload,verification):
    headers={'Content-Type':'application/json'}
    if verification is not None:headers['RequestVerificationToken']=verification
    req=urllib.request.Request(base+path,data=json.dumps(payload).encode(),headers=headers)
    try:
        with c.open(req) as response:return response.status,response.read().decode()
    except urllib.error.HTTPError as error:return error.code,error.read().decode()
def post_json(path,payload):return post_json_for(user,path,payload,csrf)
status,result=post_json('/employees/count',{});assert status==200
count=json.loads(result);assert set(count)=={'policyVersion','total'} and count['total']==2
status,result=post_json('/employees/list',{'selection':['Id','Name'],'sort':[{'field':'Name'}],'limit':1})
assert status==200
data=json.loads(result);assert data['total']==2 and len(data['records'])==1
assert data['policyVersion']==count['policyVersion'] and data['hasMore'] is None
assert list(data['records'][0])==['Id','Name']
for include_total in [False,True]:
    for include_has_more in [False,True]:
        flags={'includeTotal':include_total,'includeHasMore':include_has_more}
        for endpoint in ['/employees/list','/employees/cursor']:
            query=dict(flags,selection=['Id'],limit=1)
            status,result=post_json(endpoint,query);assert status==200
            first_metadata=json.loads(result)
            assert first_metadata['total']==(2 if include_total else None)
            assert first_metadata['hasMore']==(True if include_has_more else None)
            assert len(first_metadata['records'])==1 and set(first_metadata['records'][0])=={'Id'}
            if endpoint.endswith('/cursor'):
                assert first_metadata['nextContinuationToken']
                query.update(continuationToken=first_metadata['nextContinuationToken'],
                             includeTotal=not include_total,includeHasMore=not include_has_more)
            else:query['offset']=1
            status,result=post_json(endpoint,query);assert status==200
            final_metadata=json.loads(result)
            assert final_metadata['total']==(2 if query['includeTotal'] else None)
            assert final_metadata['hasMore']==(False if query['includeHasMore'] else None)
            assert len(final_metadata['records'])==1
            assert final_metadata['records'][0]['Id']!=first_metadata['records'][0]['Id']
            if endpoint.endswith('/cursor'):assert final_metadata['nextContinuationToken'] is None
for payload in [{'selection':[]},{'sort':[]},{'offset':0},{'limit':1},{'includeTotal':False},{'includeHasMore':True}]:
    assert post_json('/employees/count',payload)[0]==400,payload
for payload in [{'offset':0},{'limit':1},{'exportBatchSize':1},{'includeTotal':False},{'includeHasMore':True},{'policyConsistency':'CapturedPolicy'},{'dataConsistency':'BestEffort'},{'maxRows':100},{'maxDuration':'00:01:00'},{'timeProvider':{}}]:
    assert post_json('/employees/export',payload)[0]==400,payload
for endpoint in ['/employees/list','/employees/cursor','/employees/count','/employees/export']:
    for payload in [{'action':'Read'},{'action':'Write'},{'action':'Aggregate'},{'unknown':True},{'identity':{}},{'context':{}},{'roles':[]},{'projectionMode':'AllowEntityMaterialization'},{'projectionExpression':'row => row'}]:
        assert post_json(endpoint,payload)[0]==400,(endpoint,payload)
for endpoint in ['/employees/list','/employees/cursor']:
    for member in ['includeTotal','includeHasMore']:
        for value in [None,0,'true',[],{}]:
            assert post_json(endpoint,{member:value})[0]==400
status,result=post_json('/employees/export',{});assert status==200
assert len(result.strip().splitlines())==2
assert all(set(json.loads(line))=={'Id','Name','Email'} for line in result.strip().splitlines())
assert post_json('/employees/count',{'filter':{'field':'Salary','operator':'eq','literal':100}})[0]==403
cursor_query={'selection':['Name'],'sort':[{'field':'Name'}],'limit':1}
status,result=post_json('/employees/cursor',cursor_query);assert status==200
first=json.loads(result);assert first['total']==2 and len(first['records'])==1
assert set(first['records'][0])=={'Name'} and first['nextContinuationToken']
resume=dict(cursor_query,continuationToken=first['nextContinuationToken'])
status,result=post_json('/employees/cursor',resume);assert status==200
final=json.loads(result);assert final['total']==2 and len(final['records'])==1
assert set(final['records'][0])=={'Name'} and final['nextContinuationToken'] is None
tampered=list(first['nextContinuationToken']);middle=len(tampered)//2
tampered[middle]='A' if tampered[middle]!='A' else 'B'
assert post_json('/employees/cursor',dict(resume,continuationToken=''.join(tampered)))[0]==400
assert post_json('/employees/cursor',dict(resume,selection=[]))[0]==400
assert post_json('/employees/cursor',{'offset':0})[0]==400
assert post_json('/employees/cursor',{'filter':{'field':'Salary','operator':'eq','literal':100}})[0]==403
assert post_json('/employees/export',{'continuationToken':first['nextContinuationToken']})[0]==400
assert post_json_for(user,'/employees/cursor',cursor_query,None)[0]==400
assert post_json_for(client(),'/employees/cursor',cursor_query,None)[0]==401
different=client();login(different,'1:hr');_,different_page=request(different,'/login')
assert post_json_for(different,'/employees/cursor',resume,token(different_page))[0]==400
status,result=post_json('/employees/cursor',{'selection':[],'limit':1});assert status==200
empty=json.loads(result);assert empty['records']==[{}] and empty['nextContinuationToken']
status,page=request(admin,'/admin');publish_csrf=token(page)
assert request(admin,'/admin/publish',{'__RequestVerificationToken':publish_csrf,'draftRevision':'2','activeRevision':'3'})[0]==200
assert post_json('/employees/cursor',resume)[0]==400
print('Lifecycle HTTP checks passed: preview isolation, stale save/publication conflicts, publication, rollback, strict operation-specific list/count/export inputs and hidden-field filter rejection.')
print('Cursor HTTP checks passed: bounded traversal, exact totals, final-token absence, empty/hidden output, tampering, query/caller/policy replay rejection and request protection.')
print('Metadata HTTP checks passed: versioned counts, default exact totals, independent nullable metadata, all offset/cursor combinations and metadata changes on resume.')
