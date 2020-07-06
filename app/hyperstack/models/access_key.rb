# frozen_string_literal: true

KEY_TYPE_DEVBUILDS = 1

# External service attachment, similar to API key but not tied to a user
class AccessKey < ApplicationRecord
  default_scope server: -> { all },
                client: -> { true }

  scope :proxy_all, server: -> { all },
        client: -> { true }

  validates :key_type, inclusion: { in: [KEY_TYPE_DEVBUILDS] }
  validates :key_code, presence: true, uniqueness: true
  validates :description, presence: true, length: { maximum: 255, minimum: 1 }

  before_validation :create_code_if_missing

  def key_type_pretty
    case key_type
    when KEY_TYPE_DEVBUILDS
      'devbuilds'
    else
      "unknown (#{key_type}"
    end
  end

  def update_last_used
    self.last_used = Time.now
    save
  end

  def create_code_if_missing
    if !key_code
      self.key_code = SecureRandom.base58 32
    end
  end

  def self.string_to_type(type)
    case type
    when "devbuilds"
      KEY_TYPE_DEVBUILDS
    else
      raise "Unknown key type: #{type}"
    end
  end
end
