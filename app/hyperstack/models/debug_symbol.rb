class DebugSymbol < ApplicationRecord

  default_scope server: -> { all },
                client: -> { true }

  scope :sort_by_created_at,
        server: -> { order('created_at DESC') },
        select: -> { sort { |a, b| b.created_at <=> a.created_at }}

  scope :sort_by_size,
        server: -> { order('size DESC') },
        select: -> { sort { |a, b| b.size <=> a.size }}

  validates :path, presence: true, uniqueness: true
  validates :symbol_hash, presence: true

  # TODO: delete file at 'path' if there is something there
end
