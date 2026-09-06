"""Loopback HTTP integration tests: no real service is contacted."""
import copy
import json
import threading
import unittest
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
import controller as c


class HTTPTests(unittest.TestCase):
    def setUp(self):
        self.events = []
        self.queue_total = 0
        self.rpc_failure = False
        self.movie_data = {'id': 1, 'path': '/downloads/library/movies/A', 'hasFile': True,
                           'monitored': True, 'qualityProfileId': 7, 'added': '2020-01-01T00:00:00Z'}
        outer = self
        class Handler(BaseHTTPRequestHandler):
            def log_message(self, *args):
                pass
            def do_GET(self):
                self.handle_request()
            def do_POST(self):
                self.handle_request()
            def do_PUT(self):
                self.handle_request()
            def do_DELETE(self):
                self.handle_request()
            def handle_request(self):
                payload = self.rfile.read(int(self.headers.get('Content-Length', 0)))
                outer.events.append((self.command, self.path, json.loads(payload) if payload else None))
                if self.path == '/transmission/rpc':
                    if self.headers.get('X-Transmission-Session-Id') != 'test-session':
                        self.send_response(409)
                        self.send_header('X-Transmission-Session-Id', 'test-session')
                        self.end_headers()
                        return
                    data = {'result': 'failure' if outer.rpc_failure else 'success', 'arguments': {'torrents': []}}
                elif self.path == '/api/v3/movie':
                    data = [outer.movie_data]
                elif self.path.startswith('/api/v3/moviefile?'):
                    data = [{'id': 11, 'movieId': 1, 'path': '/downloads/library/movies/A/a.mkv'}]
                elif self.path.startswith('/api/v3/queue?'):
                    data = {'totalRecords': outer.queue_total, 'records': []}
                elif self.path.startswith('/api/v3/movie/1'):
                    if self.command == 'PUT':
                        outer.movie_data = json.loads(payload)
                    data = outer.movie_data
                elif self.path == '/api/v3/moviefile/11' and self.command == 'DELETE':
                    data = None
                else:
                    self.send_error(404)
                    return
                self.send_response(200)
                self.send_header('Content-Type', 'application/json')
                self.end_headers()
                if data is not None:
                    self.wfile.write(json.dumps(data).encode())
        self.server = ThreadingHTTPServer(('127.0.0.1', 0), Handler)
        self.thread = threading.Thread(target=self.server.serve_forever, daemon=True)
        self.thread.start()
        self.addCleanup(self.close)
        url = 'http://127.0.0.1:%d' % self.server.server_port
        self.api = c.API(url, url + '/transmission/rpc', 'test-only-key', '', '', max_requests=30)

    def close(self):
        self.server.shutdown()
        self.server.server_close()
        self.thread.join()

    def test_full_inventory_handshake_and_radarr_payload(self):
        movies, torrents, queue = self.api.inventory()
        self.assertEqual(movies[0]['movieFile']['id'], 11)
        self.assertEqual(torrents, [])
        self.assertEqual(queue, [])
        rpc = [e for e in self.events if e[1] == '/transmission/rpc']
        self.assertEqual(len(rpc), 2)
        self.assertEqual(rpc[0][2]['method'], 'torrent-get')
        self.assertNotIn('ids', rpc[0][2]['arguments'])
        movie = self.api.movie(1)
        self.api.unmonitor(movie)
        self.assertFalse(self.api.movie(1)['monitored'])
        put = next(e for e in self.events if e[0] == 'PUT')
        self.assertIn('moveFiles=false', put[1])
        self.assertEqual(put[2]['qualityProfileId'], 7)
        self.api.delete_file(11)
        self.assertEqual(self.events[-1][:2], ('DELETE', '/api/v3/moviefile/11'))

    def test_partial_queue_rpc_failure_and_request_budget(self):
        self.queue_total = 1
        with self.assertRaises(c.SafetyError):
            self.api.inventory()
        self.queue_total = 0
        self.rpc_failure = True
        with self.assertRaises(c.SafetyError):
            self.api.inventory()
        self.api.max_requests = 0
        before = len(self.events)
        with self.assertRaises(c.SafetyError):
            self.api.movie(1)
        self.assertEqual(len(self.events), before)


if __name__ == '__main__':
    unittest.main()
