# Development-only NuGet TLS workaround for Windows sandbox. No TLS verification is disabled.
import http.server, urllib.request
class Proxy(http.server.BaseHTTPRequestHandler):
 def do_GET(self):
  try:
   url='https://'+self.path.removeprefix('/https/')
   with urllib.request.urlopen(url,timeout=90) as r:
    body=r.read(); mime=r.headers.get('Content-Type','application/octet-stream')
    if r.headers.get('Content-Encoding')=='gzip':
     import gzip
     body=gzip.decompress(body)
   if 'json' in mime:
    import json
    obj=json.loads(body)
    if isinstance(obj,dict) and 'resources' in obj: obj['resources']=[x for x in obj['resources'] if not x.get('@type','').startswith('RepositorySignatures')]
    body=json.dumps(obj).encode().replace(b'https://',b'http://127.0.0.1:8767/https/')
   self.send_response(200); self.send_header('Content-Type',mime); self.end_headers(); self.wfile.write(body)
  except Exception as e:
   self.send_error(502,str(e))
 def log_message(self,*args): pass
http.server.ThreadingHTTPServer(('127.0.0.1',8767),Proxy).serve_forever()


