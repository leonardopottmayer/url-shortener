import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { StyleProvider } from '@ant-design/cssinjs'
import { App as AntdApp, ConfigProvider } from 'antd'
import enUS from 'antd/locale/en_US'
import { UrlShortenerApp } from '@/App'
import './index.css'

const queryClient = new QueryClient()

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <QueryClientProvider client={queryClient}>
      <StyleProvider layer>
        <ConfigProvider locale={enUS}>
          <AntdApp>
            <UrlShortenerApp />
          </AntdApp>
        </ConfigProvider>
      </StyleProvider>
    </QueryClientProvider>
  </StrictMode>,
)
