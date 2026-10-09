// Local security lab only. One trusted proxy; never deploy this service publicly.
import http from 'node:http';
http.createServer((request,response)=>{
    const upstream=http.request({hostname:'petabit-qa-app',port:3000,path:request.url,method:request.method,
        headers:{...request.headers,host:'petabit-qa','x-forwarded-proto':'https','x-forwarded-for':request.socket.remoteAddress}}, result=>{
        response.writeHead(result.statusCode,result.headers);result.pipe(response);
    });
    upstream.on('error',()=>{response.writeHead(503);response.end('Lab app not ready');});
    request.pipe(upstream);
}).listen(3000,'0.0.0.0');
