class CreateReports < ActiveRecord::Migration[5.2]
  def change
    create_table :reports do |t|
      t.string :description
      t.string :notes
      t.string :extra_description
      t.timestamp :crash_time
      t.string :reporter_ip
      t.string :reporter_email
      t.boolean :public
      t.string :processed_dump
      t.string :primary_callstack
      t.string :log_files
      t.string :delete_key
      t.boolean :solved
      t.string :solved_comment
      t.references :duplicate_of, index: true

      t.timestamps
    end
  end
end
