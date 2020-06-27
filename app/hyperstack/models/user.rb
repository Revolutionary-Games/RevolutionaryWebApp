# frozen_string_literal: true

class User < ApplicationRecord
  default_scope server: -> { all },
                client: -> { true }

  scope :sort_by_created_at,
        server: -> { order('created_at DESC') },
        select: -> { sort { |a, b| b.created_at <=> a.created_at } }

  scope :paginated, lambda { |off, count|
    offset(off).take(count)
  }

  validates :email, presence: true, uniqueness: true, length: { maximum: 255 }
  validates :name, length: { maximum: 100 }, allow_nil: true

  has_secure_password validations: false
  validates :password, length: { minimum: 6 }, allow_nil: true
  validates :password, length: { maximum: 255 }, allow_nil: true
  validates :password, confirmation: true

  has_many :launcher_links

  validate :local_or_sso

  server_method :has_api_token, default: '-' do
    raise Hyperstack::AccessViolation if (acting_user != self) && !acting_user.admin?

    (!api_token.blank?).to_s
  end

  server_method :has_lfs_token, default: '-' do
    raise Hyperstack::AccessViolation if (acting_user != self) && !acting_user.admin?

    (!lfs_token.blank?).to_s
  end

  def admin?
    admin == true
  end

  def developer?
    developer == true || admin?
  end

  def name_or_email
    if name.blank?
      email
    else
      name
    end
  end

  # Clientside user getting
  def self.current
    Hyperstack::Application.acting_user_id ? find(Hyperstack::Application.acting_user_id) : nil
  end

  server_method :generate_api_token, default: '' do
    self.api_token = SecureRandom.base58(32)
    true
  end

  server_method :reset_api_token, default: '' do
    self.api_token = nil
    true
  end

  server_method :generate_lfs_token, default: '' do
    self.lfs_token = SecureRandom.base58(32)
    true
  end

  server_method :reset_lfs_token, default: '' do
    self.lfs_token = nil
    true
  end

  private

  def local_or_sso
    if (!local && sso_source.blank?) || (local && !sso_source.blank?)
      errors[:base] << 'User must be local or have SSO source set'
    end

    if local && password.blank? && !password_digest
      errors[:base] << 'Local user must have password'
    end

    if !local && (!password.blank? || password_digest)
      errors[:base] << 'SSO user may not have password'
    end
  end
end
