import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { StyleProvider } from '@ant-design/cssinjs'
import { App as AntdApp, ConfigProvider } from 'antd'
import ptBR from 'antd/locale/pt_BR'
import { UrlShortenerApp } from '@/App'
import './index.css'

const queryClient = new QueryClient()

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <QueryClientProvider client={queryClient}>
      <StyleProvider layer>
        <ConfigProvider locale={ptBR}>
          <AntdApp>
            <UrlShortenerApp />
          </AntdApp>
        </ConfigProvider>
      </StyleProvider>
    </QueryClientProvider>
  </StrictMode>,
)
