import type { NextConfig } from "next";

const nextConfig: NextConfig = {
  async headers() {
    return [
      {
        source: '/simulation/Build/:file*.gz',
        headers: [
          {
            key: 'Content-Encoding',
            value: 'gzip',
          },
        ],
      },
      {
        source: '/simulation/Build/:file*.wasm.gz',
        headers: [
          {
            key: 'Content-Type',
            value: 'application/wasm',
          },
        ],
      },
      {
        source: '/simulation/Build/:file*.js.gz',
        headers: [
          {
            key: 'Content-Type',
            value: 'application/javascript',
          },
        ],
      },
      {
        source: '/simulation/Build/:file*.data.gz',
        headers: [
          {
            key: 'Content-Type',
            value: 'application/octet-stream',
          },
        ],
      },
    ];
  },
};

export default nextConfig;
