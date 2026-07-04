import asyncio
from elasticsearch import AsyncElasticsearch
import os

ES_HOST = os.getenv('ES_HOST', 'localhost')
ES_PORT = int(os.getenv('ES_PORT', 9200))

async def test():
    es = AsyncElasticsearch([f'http://{ES_HOST}:{ES_PORT}'])
    try:
        info = await es.info()
        print('✅ Connected!')
        print(f'Cluster: {info.get("cluster_name")}')
        print(f'Version: {info.get("version", {}).get("number")}')
    except Exception as e:
        print(f'❌ Failed: {e}')
    finally:
        await es.close()

asyncio.run(test())
