# frozen_string_literal: true

# A linked launcher instance
class LauncherLink < ApplicationRecord
  belongs_to :user

  scope :sort_by_created_at,
        server: -> { order('created_at ASC') },
        select: -> { sort { |a, b| a.created_at <=> b.created_at } }

  scope :sort_by_updated_at,
        server: -> { order('created_at DESC') },
        select: -> { sort { |a, b| b.created_at <=> a.created_at } }

  validates :link_code, presence: true, uniqueness: true,
                        length: { maximum: 255, minimum: 15 }
end
