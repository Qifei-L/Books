import { spawn } from 'node:child_process';

const port = process.env.PORT || '8080';
const child = spawn('serve', ['-s', 'dist/books-ui/browser', '-l', port], {
  shell: true,
  stdio: 'inherit'
});

child.on('exit', (code) => {
  process.exit(code ?? 0);
});
