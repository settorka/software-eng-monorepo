#!/usr/bin/env python3
"""
Elasticsearch Data Seeder - Generate fake magazine articles for development.
"""
import os
import sys
import json
import asyncio
import random
from datetime import datetime, timedelta
from typing import List, Dict, Any

from elasticsearch import AsyncElasticsearch, helpers
from faker import Faker
from sentence_transformers import SentenceTransformer
import numpy as np

# ============================================================================
# Configuration
# ============================================================================

ES_HOST = os.getenv("ES_HOST", "localhost")
ES_PORT = int(os.getenv("ES_PORT", 9200))
NUM_ARTICLES = int(os.getenv("NUM_ARTICLES", 1000))
BATCH_SIZE = int(os.getenv("BATCH_SIZE", 100))
MODEL_NAME = os.getenv("MODEL_NAME", "all-MiniLM-L6-v2")

# Categories
CATEGORIES = [
    "Technology", "Science", "Business", "Health", "Travel",
    "Fashion", "Food", "Sports", "Politics", "Entertainment",
    "Education", "Environment", "Art", "Music", "Photography",
    "Design", "Architecture", "Psychology", "Philosophy", "History"
]

# Authors
AUTHORS = [
    "Jane Smith", "John Doe", "Emma Wilson", "Michael Brown",
    "Sarah Johnson", "David Lee", "Lisa Taylor", "Robert Chen",
    "Maria Garcia", "James Anderson", "Patricia Kim", "Thomas Wright",
    "Jennifer Lopez", "William Park", "Elizabeth Davis", "Richard White",
    "Susan Clark", "Joseph Hall", "Jessica Young", "Daniel Allen"
]

# Word lists for generating content
TECH_WORDS = ["AI", "ML", "cloud", "data", "algorithm", "neural", "digital", "platform", "software", "hardware"]
SCIENCE_WORDS = ["quantum", "genome", "organism", "ecosystem", "molecule", "particle", "experiment", "lab"]
BUSINESS_WORDS = ["market", "startup", "venture", "investor", "revenue", "strategy", "growth", "profit"]
HEALTH_WORDS = ["wellness", "nutrition", "fitness", "therapy", "recovery", "disease", "treatment", "health"]

# ============================================================================
# Faker Setup
# ============================================================================

fake = Faker()
Faker.seed(42)  # Reproducible

# ============================================================================
# Article Generator
# ============================================================================

def generate_article(article_id: int) -> Dict[str, Any]:
    """Generate a single fake article."""
    category = random.choice(CATEGORIES)
    
    # Choose word list based on category
    word_lists = {
        "Technology": TECH_WORDS,
        "Science": SCIENCE_WORDS,
        "Business": BUSINESS_WORDS,
        "Health": HEALTH_WORDS,
    }
    words = word_lists.get(category, TECH_WORDS + SCIENCE_WORDS + BUSINESS_WORDS)
    
    # Generate title with some category keywords
    title_words = random.sample(words, min(3, len(words))) + fake.words(nb=random.randint(2, 5))
    random.shuffle(title_words)
    title = " ".join(title_words).title()
    
    # Generate content (3-8 paragraphs)
    content_paragraphs = []
    for _ in range(random.randint(3, 8)):
        # Mix in some category words
        sentence = fake.paragraph(nb_sentences=random.randint(3, 7))
        if random.random() > 0.5:
            word = random.choice(words)
            sentence = sentence.replace(". ", f". {word} ", 1)
        content_paragraphs.append(sentence)
    content = " ".join(content_paragraphs)
    
    # Updated at (last 90 days)
    updated_at = fake.date_time_between(
        start_date=datetime.now() - timedelta(days=90),
        end_date=datetime.now()
    ).isoformat()
    
    return {
        "_id": str(article_id),
        "_index": "articles",
        "_source": {
            "id": article_id,
            "title": title,
            "author": random.choice(AUTHORS),
            "content": content,
            "category": category,
            "updated_at": updated_at,
            "created_at": updated_at,
        }
    }

def generate_vector_article(article_id: int, embedding: List[float]) -> Dict[str, Any]:
    """Generate a vector article with embedding."""
    # Reuse the same article data but add embedding
    base_article = generate_article(article_id)
    
    return {
        "_id": str(article_id),
        "_index": "articles_vector",
        "_source": {
            **base_article["_source"],
            "embedding": embedding,
        }
    }

# ============================================================================
# Index Creation
# ============================================================================

# In scripts/seed_elasticsearch.py, update the index creation:

async def create_indices(es: AsyncElasticsearch):
    """Create indices with proper mappings."""
    
    # Articles index (keyword search)
    articles_mapping = {
        "settings": {
            "number_of_shards": 2,
            "number_of_replicas": 0,
            "analysis": {
                "analyzer": {
                    "standard_analyzer": {
                        "type": "standard",
                        "stopwords": "_english_"
                    }
                }
            }
        },
        "mappings": {
            "properties": {
                "id": {"type": "integer"},
                "title": {
                    "type": "text",
                    "analyzer": "standard",
                    "fields": {
                        "keyword": {"type": "keyword"}
                    }
                },
                "author": {
                    "type": "text",
                    "analyzer": "standard",
                    "fields": {
                        "keyword": {"type": "keyword"}
                    }
                },
                "content": {
                    "type": "text",
                    "analyzer": "standard"
                },
                "category": {
                    "type": "text",
                    "fields": {
                        "keyword": {"type": "keyword"}
                    }
                },
                "updated_at": {"type": "date"},
                "created_at": {"type": "date"}
            }
        }
    }
    
    # Vector index (with HNSW)
    vector_mapping = {
        "settings": {
            "number_of_shards": 2,
            "number_of_replicas": 0,
        },
        "mappings": {
            "properties": {
                "id": {"type": "integer"},
                "title": {"type": "text", "fields": {"keyword": {"type": "keyword"}}},
                "author": {"type": "text", "fields": {"keyword": {"type": "keyword"}}},
                "content": {"type": "text"},
                "category": {"type": "text", "fields": {"keyword": {"type": "keyword"}}},
                "updated_at": {"type": "date"},
                "created_at": {"type": "date"},
                "embedding": {
                    "type": "dense_vector",
                    "dims": 384,
                    "index": True,
                    "similarity": "cosine",
                    "index_options": {
                        "type": "hnsw",
                        "m": 16,
                        "ef_construction": 200
                    }
                }
            }
        }
    }
    
    # Delete existing indices
    for index in ["articles", "articles_vector"]:
        try:
            await es.indices.delete(index=index, ignore_unavailable=True)
            print(f"Deleted index: {index}")
        except:
            pass
    
    # Create indices
    await es.indices.create(index="articles", body=articles_mapping)
    await es.indices.create(index="articles_vector", body=vector_mapping)
    
    print("Created indices: articles, articles_vector")

# ============================================================================
# Data Seeding
# ============================================================================

async def seed_data(es: AsyncElasticsearch, model: SentenceTransformer, num_articles: int):
    """Seed Elasticsearch with fake data."""
    print(f"Generating {num_articles} articles...")
    
    # Generate all articles first
    articles = []
    for i in range(1, num_articles + 1):
        article = generate_article(i)
        articles.append(article)
    
    # Bulk index articles
    print("Indexing articles (keyword index)...")
    success, errors = await helpers.async_bulk(
        es,
        articles,
        chunk_size=BATCH_SIZE,
        request_timeout=60
    )
    print(f"Indexed {success} articles to 'articles'")
    if errors:
        print(f"Errors: {errors[:5]}...")
    
    # Generate embeddings and vector articles
    print("Generating embeddings...")
    vector_articles = []
    
    # Process in batches for efficiency
    for i in range(0, len(articles), BATCH_SIZE):
        batch = articles[i:i + BATCH_SIZE]
        
        # Get content for embeddings
        contents = [a["_source"]["content"] for a in batch]
        
        # Generate embeddings in batch
        embeddings = model.encode(contents)
        
        # Create vector articles
        for article, embedding in zip(batch, embeddings):
            vector_articles.append({
                "_id": article["_id"],
                "_index": "articles_vector",
                "_source": {
                    **article["_source"],
                    "embedding": embedding.tolist()
                }
            })
        
        print(f"Processed batch {i//BATCH_SIZE + 1}/{(len(articles)//BATCH_SIZE) + 1}")
    
    # Bulk index vector articles
    print("Indexing articles (vector index)...")
    success, errors = await helpers.async_bulk(
        es,
        vector_articles,
        chunk_size=BATCH_SIZE,
        request_timeout=120
    )
    print(f"Indexed {success} articles to 'articles_vector'")
    if errors:
        print(f"Errors: {errors[:5]}...")
    
    # Refresh indices
    await es.indices.refresh(index="articles")
    await es.indices.refresh(index="articles_vector")
    print("Indices refreshed")

# ============================================================================
# Main
# ============================================================================

async def main():
    """Main entry point."""
    print("=" * 60)
    print("Elasticsearch Data Seeder")
    print("=" * 60)
    
    # Connect to Elasticsearch
    es = AsyncElasticsearch([f"http://{ES_HOST}:{ES_PORT}"])
    
    try:
        # Wait for Elasticsearch to be ready
        for attempt in range(30):
            try:
                await es.info()
                break
            except:
                print(f"Waiting for Elasticsearch... ({attempt+1}/30)")
                await asyncio.sleep(2)
        else:
            print("❌ Elasticsearch not available after 30 attempts")
            sys.exit(1)
        
        print(f"✅ Connected to Elasticsearch at {ES_HOST}:{ES_PORT}")
        
        # Create indices
        await create_indices(es)
        
        # Load model for embeddings
        print(f"Loading model: {MODEL_NAME}")
        model = SentenceTransformer(MODEL_NAME)
        print("✅ Model loaded")
        
        # Seed data
        await seed_data(es, model, NUM_ARTICLES)
        
        # Verify
        count = await es.count(index="articles")
        print(f"Total articles: {count['count']}")
        
        count_vector = await es.count(index="articles_vector")
        print(f"Total vector articles: {count_vector['count']}")
        
        print("\n✅ Seeding complete!")
        
    except Exception as e:
        print(f"\n❌ Error: {e}")
        import traceback
        traceback.print_exc()
        sys.exit(1)
    finally:
        await es.close()

if __name__ == "__main__":
    asyncio.run(main())