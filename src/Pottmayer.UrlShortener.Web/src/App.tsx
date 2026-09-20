import { useState } from 'react'
import { useMutation, useQuery } from '@tanstack/react-query'
import {
  App,
  Button,
  Card,
  Form,
  Input,
  Layout,
  Modal,
  Space,
  Statistic,
  Table,
  Typography,
} from 'antd'
import type { ColumnsType } from 'antd/es/table'
import {
  BarChartOutlined,
  CopyOutlined,
  DeleteOutlined,
  LinkOutlined,
} from '@ant-design/icons'
import axios from 'axios'
import { getStats, shorten } from '@/api'
import { useMyLinks, type MyLink } from '@/useMyLinks'

const { Header, Content } = Layout
const { Title, Paragraph, Text } = Typography

function errorMessage(error: unknown): string {
  if (axios.isAxiosError(error)) {
    const data = error.response?.data as { error?: string } | undefined
    return data?.error ?? error.message
  }
  return 'Unexpected error.'
}

interface ShortenForm {
  longUrl: string
  customAlias?: string
}

export function UrlShortenerApp() {
  const { message } = App.useApp()
  const [form] = Form.useForm<ShortenForm>()
  const { links, addLink, removeLink } = useMyLinks()
  const [statsCode, setStatsCode] = useState<string | null>(null)

  const shortenMutation = useMutation({
    mutationFn: (values: ShortenForm) => shorten(values.longUrl, values.customAlias),
    onSuccess: (result, values) => {
      addLink({
        code: result.code,
        longUrl: values.longUrl,
        shortUrl: result.shortUrl,
        createdAt: new Date().toISOString(),
      })
      message.success(`Short link created: ${result.code}`)
      form.resetFields()
    },
    onError: (error) => message.error(errorMessage(error)),
  })

  const copy = async (value: string) => {
    try {
      await navigator.clipboard.writeText(value)
      message.success('Copied.')
    } catch {
      message.error('Could not copy.')
    }
  }

  const columns: ColumnsType<MyLink> = [
    {
      title: 'Short URL',
      dataIndex: 'shortUrl',
      render: (shortUrl: string, link) => (
        <Space>
          <a href={shortUrl} target="_blank" rel="noreferrer">
            <LinkOutlined /> {link.code}
          </a>
          <Button type="text" size="small" icon={<CopyOutlined />} onClick={() => copy(shortUrl)} />
        </Space>
      ),
    },
    {
      title: 'Target',
      dataIndex: 'longUrl',
      ellipsis: true,
      render: (longUrl: string) => (
        <Text type="secondary" ellipsis>
          {longUrl}
        </Text>
      ),
    },
    {
      title: 'Actions',
      key: 'actions',
      width: 160,
      render: (_, link) => (
        <Space>
          <Button size="small" icon={<BarChartOutlined />} onClick={() => setStatsCode(link.code)}>
            Stats
          </Button>
          <Button
            size="small"
            danger
            icon={<DeleteOutlined />}
            onClick={() => removeLink(link.code)}
          />
        </Space>
      ),
    },
  ]

  return (
    <Layout className="min-h-full">
      <Header className="flex items-center">
        <Title level={4} style={{ color: '#fff', margin: 0 }}>
          URL Shortener
        </Title>
      </Header>

      <Content className="mx-auto w-full max-w-3xl p-4">
        <Paragraph type="secondary" className="mt-2">
          System design lab — gateway, KGS, cache, messaging and analytics on top of the Tars framework.
        </Paragraph>

        <Card title="Shorten URL" className="mb-4">
          <Form
            form={form}
            layout="vertical"
            onFinish={(values) => shortenMutation.mutate(values)}
          >
            <Form.Item
              label="Long URL"
              name="longUrl"
              rules={[
                { required: true, message: 'Enter the URL.' },
                { type: 'url', message: 'Invalid URL (include http:// or https://).' },
              ]}
            >
              <Input placeholder="https://example.com/a/very/long/url" />
            </Form.Item>

            <Form.Item
              label="Custom alias (optional)"
              name="customAlias"
              rules={[{ pattern: /^[0-9A-Za-z]{3,16}$/, message: '3-16 characters [0-9A-Za-z].' }]}
            >
              <Input placeholder="my-alias" />
            </Form.Item>

            <Button type="primary" htmlType="submit" loading={shortenMutation.isPending}>
              Shorten
            </Button>
          </Form>
        </Card>

        <Card title="My links">
          <Table<MyLink>
            rowKey="code"
            columns={columns}
            dataSource={links}
            pagination={false}
            locale={{ emptyText: 'No links yet. Shorten one above.' }}
          />
        </Card>
      </Content>

      <StatsModal code={statsCode} onClose={() => setStatsCode(null)} />
    </Layout>
  )
}

function StatsModal({ code, onClose }: { code: string | null; onClose: () => void }) {
  const query = useQuery({
    queryKey: ['stats', code],
    queryFn: () => getStats(code!),
    enabled: code !== null,
  })

  return (
    <Modal open={code !== null} onCancel={onClose} onOk={onClose} title={`Statistics — ${code ?? ''}`}>
      <Statistic
        title="Total clicks"
        value={query.data?.totalClicks ?? 0}
        loading={query.isFetching}
      />
    </Modal>
  )
}
