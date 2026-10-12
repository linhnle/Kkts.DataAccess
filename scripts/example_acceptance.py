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
def post_json(path,payload):
    req=urllib.request.Request(base+path,data=json.dumps(payload).encode(),headers={'Content-Type':'application/json','RequestVerificationToken':csrf})
    try:
        with user.open(req) as response:return response.status,response.read().decode()
    except urllib.error.HTTPError as error:return error.code,error.read().decode()
status,result=post_json('/employees/count',{'limit':1});assert status==200 and json.loads(result)==2
status,result=post_json('/employees/export',{});assert status==200
assert len(result.strip().splitlines())==2
assert post_json('/employees/count',{'filter':{'field':'Salary','operator':'eq','literal':100}})[0]==403
print('Lifecycle HTTP checks passed: preview isolation, stale save/publication conflicts, publication, rollback, count/export and hidden-field filter rejection.')
